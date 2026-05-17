using MailMopper.Data;
using MailMopper.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Transforms;

namespace MailMopper.Services;

/// <summary>
/// Trains an ML.NET text classifier on rule-classified emails, the user's whitelist,
/// and prior human review decisions. Closes the feedback loop so that user corrections
/// teach future runs.
/// </summary>
public class ModelTrainerService
{
    // Cap any class to this multiple of the median class size before training, so
    // that majority classes (typically Notification / Marketing) do not dominate
    // the prior of the multinomial model.
    private const int MaxClassImbalanceMultiplier = 3;

    // Minimum samples per class. Below this we warn the user that the model may
    // not generalise for that category.
    private const int MinSamplesPerClassWarning = 20;

    // Per-class recall below this in cross-validation triggers a warning.
    private const double LowRecallWarningThreshold = 0.5;

    private readonly AppDbContext _db;

    public ModelTrainerService(AppDbContext db) => _db = db;

    /// <summary>
    /// Trains the email classifier and saves it to the specified path.
    /// Returns training metrics.
    /// </summary>
    public async Task<TrainingResult> TrainAsync(string modelPath, Action<string>? onStatus, CancellationToken ct)
    {
        onStatus?.Invoke("Loading training data from database...");

        var trainingData = await BuildTrainingDataAsync(onStatus, ct);

        if (trainingData.Count < 100)
        {
            throw new InvalidOperationException(
                $"Insufficient training data: {trainingData.Count} emails. Need at least 100 labelled emails. " +
                "Run 'classify --skip-ml' first, then mark a few personal emails as Keep in 'review'.");
        }

        // Down-sample over-represented classes so the trainer doesn't learn a
        // pathological prior toward Notification / Marketing.
        trainingData = BalanceClasses(trainingData, onStatus);

        onStatus?.Invoke($"Loaded {trainingData.Count:N0} training samples across {trainingData.Select(t => t.Label).Distinct().Count()} categories");

        var mlContext = new MLContext(seed: 42);
        var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

        var categories = trainingData.Select(t => t.Label).Distinct().OrderBy(l => l).ToList();

        // Build the ML pipeline.
        // Text features come from From / Subject / CleanSubject (prefix-stripped) / Snippet.
        // Categorical features (FromDomain, GmailCategory) are one-hot hashed to keep the
        // model bounded. Boolean signals (HasListUnsubscribe, IsForwardedOrReply) are
        // already floats and concatenate directly.
        var pipeline = mlContext.Transforms.Text.FeaturizeText("FromFeatures", nameof(EmailFeature.From))
            .Append(mlContext.Transforms.Text.FeaturizeText("SubjectFeatures", nameof(EmailFeature.Subject)))
            .Append(mlContext.Transforms.Text.FeaturizeText("CleanSubjectFeatures", nameof(EmailFeature.CleanSubject)))
            .Append(mlContext.Transforms.Text.FeaturizeText("SnippetFeatures", nameof(EmailFeature.Snippet)))
            .Append(mlContext.Transforms.Categorical.OneHotHashEncoding("FromDomainFeatures", nameof(EmailFeature.FromDomain), numberOfBits: 12))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("GmailCategoryFeatures", nameof(EmailFeature.GmailCategory), OneHotEncodingEstimator.OutputKind.Indicator))
            .Append(mlContext.Transforms.Concatenate(
                "Features",
                "FromFeatures",
                "SubjectFeatures",
                "CleanSubjectFeatures",
                "SnippetFeatures",
                "FromDomainFeatures",
                "GmailCategoryFeatures",
                nameof(EmailFeature.HasListUnsubscribe),
                nameof(EmailFeature.IsForwardedOrReply)))
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.Transforms.Conversion.MapValueToKey("Label"))
            // LbfgsMaximumEntropy tends to produce better-calibrated probabilities
            // than SDCA on small/medium imbalanced text problems, which makes the
            // MinConfidence threshold in MlClassifier meaningful.
            .Append(mlContext.MulticlassClassification.Trainers.LbfgsMaximumEntropy("Label", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        // Cross-validate to get metrics before final training
        onStatus?.Invoke("Cross-validating (5-fold)...");
        var cvResults = mlContext.MulticlassClassification.CrossValidate(dataView, pipeline, numberOfFolds: 5, labelColumnName: "Label");

        var avgAccuracy = cvResults.Average(r => r.Metrics.MacroAccuracy);
        var avgLogLoss = cvResults.Average(r => r.Metrics.LogLoss);

        onStatus?.Invoke($"Cross-validation: Accuracy={avgAccuracy:P1}, LogLoss={avgLogLoss:F3}");

        // Gather per-class metrics from the best fold
        var bestFold = cvResults.OrderByDescending(r => r.Metrics.MacroAccuracy).First();
        var perClassMetrics = new Dictionary<string, (double Precision, double Recall, double F1)>();
        var confusionMatrix = bestFold.Metrics.ConfusionMatrix;
        var classNames = confusionMatrix.PerClassPrecision.Select((_, i) => i < categories.Count
            ? categories[i]
            : $"Class{i}").ToList();

        for (int i = 0; i < confusionMatrix.PerClassPrecision.Count; i++)
        {
            var name = i < classNames.Count ? classNames[i] : $"Class{i}";
            perClassMetrics[name] = (
                confusionMatrix.PerClassPrecision[i],
                confusionMatrix.PerClassRecall[i],
                2 * confusionMatrix.PerClassPrecision[i] * confusionMatrix.PerClassRecall[i] /
                    (confusionMatrix.PerClassPrecision[i] + confusionMatrix.PerClassRecall[i] + 1e-10)
            );
        }

        // Surface low-quality classes as warnings — these are the categories most
        // likely to bleed into the user's "personal email mis-classified" complaint.
        var sampleCounts = trainingData.GroupBy(t => t.Label).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (name, metrics) in perClassMetrics.OrderBy(x => x.Key))
        {
            if (sampleCounts.TryGetValue(name, out var n) && n < MinSamplesPerClassWarning)
                onStatus?.Invoke($"  ⚠ Few samples for '{name}' ({n}). Mark more emails in 'review' to improve.");
            if (metrics.Recall < LowRecallWarningThreshold)
                onStatus?.Invoke($"  ⚠ Low recall for '{name}' ({metrics.Recall:P0}). Model often misses this category.");
        }

        // Train final model on all data
        onStatus?.Invoke("Training final model on all data...");
        var model = pipeline.Fit(dataView);

        // Save model
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath) ?? ".");
        mlContext.Model.Save(model, dataView.Schema, modelPath);

        var fileSize = new FileInfo(modelPath).Length;
        onStatus?.Invoke($"Model saved to {modelPath} ({fileSize / 1024.0 / 1024.0:F1} MB)");

        return new TrainingResult(
            TrainingSamples: trainingData.Count,
            Categories: categories,
            Accuracy: avgAccuracy,
            LogLoss: avgLogLoss,
            PerClassMetrics: perClassMetrics,
            ModelPath: modelPath,
            ModelSizeBytes: fileSize);
    }

    /// <summary>
    /// Returns the default model path.
    /// </summary>
    public static string GetDefaultModelPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "MailMopper", "email_classifier.zip");
    }

    /// <summary>
    /// Builds the labelled training set by unioning three sources:
    ///   1. Rule-classified emails (existing behaviour) — high precision.
    ///   2. Whitelisted senders → Keep — explicit user signal that we previously threw away.
    ///   3. Human review decisions (Keep / Whitelisted) → Keep — closes the feedback loop.
    /// Forwarded/replied emails that were rule-labelled are filtered out so they don't
    /// teach the model that personal-looking forwards are notifications.
    /// Internal so it can be unit-tested in isolation.
    /// </summary>
    internal async Task<List<EmailFeature>> BuildTrainingDataAsync(Action<string>? onStatus, CancellationToken ct)
    {
        // 1. Rule-classified rows
        var ruleLabelled = await _db.Classifications
            .Include(c => c.Email)
            .Where(c => c.Email != null && c.ClassifiedBy == "rule" && c.Category != ClassificationCategory.Unclassified)
            .ToListAsync(ct);

        // 2. Whitelisted senders → Keep
        var whitelistDomains = await _db.Whitelist
            .Where(w => w.PatternType == "domain")
            .Select(w => w.Pattern)
            .ToListAsync(ct);
        var whitelistEmails = await _db.Whitelist
            .Where(w => w.PatternType == "email")
            .Select(w => w.Pattern)
            .ToListAsync(ct);

        var whitelistDomainSet = new HashSet<string>(whitelistDomains, StringComparer.OrdinalIgnoreCase);
        var whitelistEmailSet = new HashSet<string>(whitelistEmails, StringComparer.OrdinalIgnoreCase);

        var whitelistKeepEmails = whitelistDomainSet.Count == 0 && whitelistEmailSet.Count == 0
            ? new List<EmailRecord>()
            : await _db.Emails
                .Where(e => whitelistDomainSet.Contains(e.FromDomain) || whitelistEmailSet.Contains(e.From))
                .ToListAsync(ct);

        // 3. Human review decisions: Keep + Whitelisted are unambiguous "Keep" examples.
        //    ApproveTrash is intentionally NOT used as a label source: the user is endorsing
        //    the existing predicted category, which we already trained on via the rule path.
        var humanKeep = await _db.Classifications
            .Include(c => c.Email)
            .Where(c => c.Email != null &&
                        (c.ReviewDecision == ReviewDecision.Keep || c.ReviewDecision == ReviewDecision.Whitelisted))
            .ToListAsync(ct);

        var rows = new Dictionary<string, EmailFeature>(StringComparer.Ordinal);

        // Whitelist + human Keep take precedence over rule labels for the same MessageId,
        // because the user has explicitly told us "this is personal".
        foreach (var c in ruleLabelled)
        {
            // Don't train on forwarded/replied emails that happened to match a rule —
            // they pollute the model with mislabelled examples (see RuleClassifier).
            if (EmailHeuristics.IsForwardOrReply(c.Email!.Subject, c.Email.Snippet))
                continue;

            rows[c.MessageId] = MlClassifier.BuildFeature(c.Email!, c.Category.ToString());
        }

        foreach (var email in whitelistKeepEmails)
            rows[email.MessageId] = MlClassifier.BuildFeature(email, ClassificationCategory.Keep.ToString());

        foreach (var c in humanKeep)
            rows[c.MessageId] = MlClassifier.BuildFeature(c.Email!, ClassificationCategory.Keep.ToString());

        if (whitelistKeepEmails.Count > 0)
            onStatus?.Invoke($"Added {whitelistKeepEmails.Count} whitelisted-sender emails as Keep examples");
        if (humanKeep.Count > 0)
            onStatus?.Invoke($"Added {humanKeep.Count} human-reviewed Keep/Whitelisted emails");

        return rows.Values.ToList();
    }

    /// <summary>
    /// Caps every class to <see cref="MaxClassImbalanceMultiplier"/> times the median
    /// class size. This stops the majority class (usually Notification) from drowning
    /// out minority classes (especially the new Keep class) in the model prior.
    /// </summary>
    private static List<EmailFeature> BalanceClasses(List<EmailFeature> data, Action<string>? onStatus)
    {
        var groups = data.GroupBy(d => d.Label).ToList();
        if (groups.Count <= 1)
            return data;

        var sizes = groups.Select(g => g.Count()).OrderBy(n => n).ToList();
        var median = sizes[sizes.Count / 2];
        var cap = Math.Max(MinSamplesPerClassWarning, median * MaxClassImbalanceMultiplier);

        // Deterministic shuffle so cross-validation is repeatable.
        var rng = new Random(42);
        var balanced = new List<EmailFeature>(data.Count);

        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count > cap)
            {
                onStatus?.Invoke($"  Down-sampling '{group.Key}' from {items.Count} → {cap} (median {median})");
                // Fisher-Yates partial shuffle: take `cap` random items
                for (int i = 0; i < cap; i++)
                {
                    int j = rng.Next(i, items.Count);
                    (items[i], items[j]) = (items[j], items[i]);
                }
                balanced.AddRange(items.Take(cap));
            }
            else
            {
                balanced.AddRange(items);
            }
        }

        return balanced;
    }
}

public record TrainingResult(
    int TrainingSamples,
    List<string> Categories,
    double Accuracy,
    double LogLoss,
    Dictionary<string, (double Precision, double Recall, double F1)> PerClassMetrics,
    string ModelPath,
    long ModelSizeBytes);

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MailMopper.Config;
using MailMopper.Models;

namespace MailMopper.Services;

public record ClassificationResult(
    ClassificationCategory Category,
    string RuleName,
    double Confidence);

public class RuleClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppSettings _appSettings;
    private List<Rule> _rules = [];
    private readonly Dictionary<string, Regex> _compiledRegexCache = [];

    public RuleClassifier(AppSettings appSettings)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
    }

    public async Task LoadRulesAsync(CancellationToken ct)
    {
        var rulesPath = _appSettings.Classification.RulesPath;

        if (!File.Exists(rulesPath))
        {
            throw new FileNotFoundException($"Rules file not found at {rulesPath}");
        }

        var json = await File.ReadAllTextAsync(rulesPath, ct);

        var rulesDocument = JsonSerializer.Deserialize<RulesDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize rules file");

        _rules = rulesDocument.Rules
            .OrderBy(r => r.Priority)
            .ToList();

        // Pre-compile all regex patterns for performance
        CompileRegexPatterns();
    }

    public ClassificationResult? Classify(EmailRecord email)
    {
        foreach (var rule in _rules)
        {
            if (MatchesRule(email, rule))
            {
                if (!Enum.TryParse<ClassificationCategory>(rule.Category, ignoreCase: true, out var category))
                {
                    category = ClassificationCategory.Unclassified;
                }

                return new ClassificationResult(
                    Category: category,
                    RuleName: rule.Name ?? rule.Type,
                    Confidence: 1.0);
            }
        }

        return null;
    }

    public IReadOnlyList<(EmailRecord email, ClassificationResult result)> ClassifyAll(
        IEnumerable<EmailRecord> emails)
    {
        var results = new List<(EmailRecord, ClassificationResult)>();

        foreach (var email in emails)
        {
            var classification = Classify(email);
            if (classification != null)
            {
                results.Add((email, classification));
            }
        }

        return results.AsReadOnly();
    }

    private bool MatchesRule(EmailRecord email, Rule rule) =>
        rule.Type switch
        {
            "header" => MatchesHeaderRule(email, rule),
            "gmail-category" => MatchesGmailCategoryRule(email, rule),
            "sender-domain" => MatchesSenderDomainRule(email, rule),
            "sender-pattern" => MatchesSenderPatternRule(email, rule),
            "subject-pattern" => MatchesSubjectPatternRule(email, rule),
            _ => false
        };

    private static bool MatchesHeaderRule(EmailRecord email, Rule rule)
    {
        var condition = rule.Condition as JsonElement?;
        if (!condition.HasValue)
            return false;

        if (condition.Value.TryGetProperty("header", out var headerElement) &&
            condition.Value.TryGetProperty("present", out var presentElement))
        {
            var headerName = headerElement.GetString();
            var shouldBePresent = presentElement.GetBoolean();

            if (headerName == "List-Unsubscribe")
            {
                return email.HasListUnsubscribe == shouldBePresent;
            }
        }

        return false;
    }

    private static bool MatchesGmailCategoryRule(EmailRecord email, Rule rule)
    {
        var condition = rule.Condition as JsonElement?;
        if (!condition.HasValue)
            return false;

        if (condition.Value.TryGetProperty("category", out var categoryElement))
        {
            var targetCategory = categoryElement.GetString();
            return !string.IsNullOrEmpty(email.GmailCategory) &&
                   email.GmailCategory.Equals(targetCategory, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool MatchesSenderDomainRule(EmailRecord email, Rule rule)
    {
        var condition = rule.Condition as JsonElement?;
        if (!condition.HasValue)
            return false;

        if (condition.Value.TryGetProperty("domains", out var domainsElement))
        {
            var domains = domainsElement.EnumerateArray()
                .Select(d => d.GetString())
                .Where(d => !string.IsNullOrEmpty(d))
                .ToList();

            var emailDomain = email.FromDomain?.ToLowerInvariant();
            if (string.IsNullOrEmpty(emailDomain))
                return false;

            return domains.Any(d => emailDomain.Equals(d?.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private bool MatchesSenderPatternRule(EmailRecord email, Rule rule)
    {
        var condition = rule.Condition as JsonElement?;
        if (!condition.HasValue)
            return false;

        if (condition.Value.TryGetProperty("patterns", out var patternsElement))
        {
            var patterns = patternsElement.EnumerateArray()
                .Select(p => p.GetString())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (string.IsNullOrEmpty(email.From))
                return false;

            return patterns.Any(pattern => MatchesPattern(email.From, pattern!));
        }

        return false;
    }

    private bool MatchesSubjectPatternRule(EmailRecord email, Rule rule)
    {
        var condition = rule.Condition as JsonElement?;
        if (!condition.HasValue)
            return false;

        if (condition.Value.TryGetProperty("patterns", out var patternsElement))
        {
            var patterns = patternsElement.EnumerateArray()
                .Select(p => p.GetString())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (string.IsNullOrEmpty(email.Subject))
                return false;

            return patterns.Any(pattern => MatchesPattern(email.Subject, pattern!));
        }

        return false;
    }

    private bool MatchesPattern(string input, string pattern)
    {
        if (!_compiledRegexCache.TryGetValue(pattern, out var regex))
        {
            return false; // Pattern not pre-compiled
        }

        return regex.IsMatch(input);
    }

    private void CompileRegexPatterns()
    {
        _compiledRegexCache.Clear();

        foreach (var rule in _rules)
        {
            var condition = rule.Condition as JsonElement?;
            if (!condition.HasValue)
                continue;

            var patterns = new List<string>();

            // Extract patterns from sender-pattern rules
            if (rule.Type == "sender-pattern" &&
                condition.Value.TryGetProperty("patterns", out var senderPatterns))
            {
                patterns.AddRange(senderPatterns.EnumerateArray()
                    .Select(p => p.GetString())
                    .Where(p => !string.IsNullOrEmpty(p))!);
            }

            // Extract patterns from subject-pattern rules
            if (rule.Type == "subject-pattern" &&
                condition.Value.TryGetProperty("patterns", out var subjectPatterns))
            {
                patterns.AddRange(subjectPatterns.EnumerateArray()
                    .Select(p => p.GetString())
                    .Where(p => !string.IsNullOrEmpty(p))!);
            }

            // Compile each pattern with case-insensitive option
            foreach (var pattern in patterns)
            {
                if (!_compiledRegexCache.ContainsKey(pattern))
                {
                    try
                    {
                        _compiledRegexCache[pattern] = new Regex(
                            pattern,
                            RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    }
                    catch (ArgumentException)
                    {
                        // Invalid regex pattern, skip it
                    }
                }
            }
        }
    }

    // Internal classes for JSON deserialization
    private class RulesDocument
    {
        [JsonPropertyName("rules")]
        public List<Rule> Rules { get; set; } = [];
    }

    private class Rule
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("condition")]
        public JsonElement Condition { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("priority")]
        public int Priority { get; set; }
    }
}

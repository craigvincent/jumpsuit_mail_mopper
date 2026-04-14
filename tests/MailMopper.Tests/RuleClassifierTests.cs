using MailMopper.Config;
using MailMopper.Models;
using MailMopper.Services;

namespace MailMopper.Tests;

public class RuleClassifierTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RuleClassifier _classifier;

    public RuleClassifierTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MAIL_MOPPER_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var rulesSource = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules", "default-rules.json");
        rulesSource = Path.GetFullPath(rulesSource);
        var rulesDest = Path.Combine(_tempDir, "default-rules.json");
        File.Copy(rulesSource, rulesDest);

        var settings = new AppSettings
        {
            Classification = new ClassificationSettings { RulesPath = rulesDest }
        };

        _classifier = new RuleClassifier(settings);
        _classifier.LoadRulesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    private static EmailRecord MakeEmail(
        string? from = null,
        string? fromDomain = null,
        string? subject = null,
        bool hasListUnsubscribe = false,
        string? gmailCategory = null) => new()
        {
            MessageId = Guid.NewGuid().ToString(),
            From = from ?? "someone@example.com",
            FromDomain = fromDomain ?? "example.com",
            Subject = subject ?? "Hello",
            HasListUnsubscribe = hasListUnsubscribe,
            GmailCategory = gmailCategory ?? "",
            GmailLabels = "",
            Date = DateTimeOffset.UtcNow,
            Snippet = ""
        };

    [Fact]
    public void ClassifiesEmailWithListUnsubscribeHeader()
    {
        var email = MakeEmail(hasListUnsubscribe: true);

        var result = _classifier.Classify(email);

        Assert.NotNull(result);
        Assert.Equal(ClassificationCategory.Newsletter, result.Category);
    }

    [Fact]
    public void ClassifiesGmailPromotionsCategory()
    {
        var email = MakeEmail(gmailCategory: "CATEGORY_PROMOTIONS");

        var result = _classifier.Classify(email);

        Assert.NotNull(result);
        Assert.Equal(ClassificationCategory.Marketing, result.Category);
    }

    [Fact]
    public void ClassifiesGmailSocialCategory()
    {
        var email = MakeEmail(gmailCategory: "CATEGORY_SOCIAL");

        var result = _classifier.Classify(email);

        Assert.NotNull(result);
        Assert.Equal(ClassificationCategory.Social, result.Category);
    }

    [Fact]
    public void ClassifiesKnownMarketingDomain()
    {
        var email = MakeEmail(fromDomain: "mailchimp.com");

        var result = _classifier.Classify(email);

        Assert.NotNull(result);
        Assert.Equal(ClassificationCategory.Marketing, result.Category);
    }

    [Fact]
    public void ClassifiesNoreplyPattern()
    {
        var email = MakeEmail(from: "noreply@example.com");

        var result = _classifier.Classify(email);

        Assert.NotNull(result);
        Assert.Equal(ClassificationCategory.Automated, result.Category);
    }

    [Fact]
    public void ClassifiesNewsletterSubjectPattern()
    {
        var email = MakeEmail(subject: "Your Weekly Digest for March");

        var result = _classifier.Classify(email);

        Assert.NotNull(result);
        Assert.Equal(ClassificationCategory.Newsletter, result.Category);
    }

    [Fact]
    public void DoesNotClassifyPersonalEmail()
    {
        var email = MakeEmail(
            from: "jane.doe@gmail.com",
            fromDomain: "gmail.com",
            subject: "Hey, want to grab lunch?");

        var result = _classifier.Classify(email);

        Assert.Null(result);
    }

    [Fact]
    public void HigherPriorityRuleWins()
    {
        // List-Unsubscribe header rule (priority 10) should beat gmail-category (priority 20)
        var email = MakeEmail(
            hasListUnsubscribe: true,
            gmailCategory: "CATEGORY_PROMOTIONS");

        var result = _classifier.Classify(email);

        Assert.NotNull(result);
        Assert.Equal(ClassificationCategory.Newsletter, result.Category);
        Assert.Equal("list-unsubscribe-header", result.RuleName);
    }
}

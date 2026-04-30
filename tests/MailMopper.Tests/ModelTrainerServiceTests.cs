using MailMopper.Data;
using MailMopper.Models;
using MailMopper.Services;
using Microsoft.EntityFrameworkCore;

namespace MailMopper.Tests;

/// <summary>
/// Unit tests for <see cref="ModelTrainerService.BuildTrainingDataAsync"/> — the
/// data-source half of the trainer. The actual ML.NET pipeline isn't exercised
/// here (slow, non-deterministic for tiny inputs); it has its own end-to-end
/// behaviour validated indirectly through the classify pipeline.
/// </summary>
public class ModelTrainerServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ModelTrainerService _trainer;

    public ModelTrainerServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _trainer = new ModelTrainerService(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static EmailRecord MakeEmail(
        string id,
        string from = "sender@example.com",
        string? fromDomain = null,
        string subject = "Hello",
        string snippet = "",
        string gmailCategory = "",
        bool hasListUnsubscribe = false) => new()
        {
            MessageId = id,
            From = from,
            FromDomain = fromDomain ?? from.Split('@').LastOrDefault() ?? "unknown",
            Subject = subject,
            Snippet = snippet,
            GmailCategory = gmailCategory,
            GmailLabels = "",
            HasListUnsubscribe = hasListUnsubscribe,
            Date = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task BuildTrainingData_IncludesRuleClassifiedEmails()
    {
        _db.Emails.Add(MakeEmail("m1", "newsletter@example.com"));
        _db.Classifications.Add(new Classification
        {
            MessageId = "m1",
            Category = ClassificationCategory.Newsletter,
            ClassifiedBy = "rule",
        });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        var row = Assert.Single(data);
        Assert.Equal(ClassificationCategory.Newsletter.ToString(), row.Label);
        Assert.Equal("m1", row.MessageId);
    }

    [Fact]
    public async Task BuildTrainingData_ExcludesUnclassifiedAndNonRuleRows()
    {
        _db.Emails.AddRange(MakeEmail("u1"), MakeEmail("ml1"));
        _db.Classifications.AddRange(
            new Classification { MessageId = "u1", Category = ClassificationCategory.Unclassified, ClassifiedBy = "rule" },
            new Classification { MessageId = "ml1", Category = ClassificationCategory.Notification, ClassifiedBy = "ml" });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        Assert.Empty(data);
    }

    [Fact]
    public async Task BuildTrainingData_ExcludesForwardedRuleClassifiedEmails()
    {
        // A rule mistakenly labelled a forward — must NOT pollute training set.
        _db.Emails.AddRange(
            MakeEmail("normal", subject: "your order has been shipped"),
            MakeEmail("fwd", subject: "Fwd: your order has been shipped"));
        _db.Classifications.AddRange(
            new Classification { MessageId = "normal", Category = ClassificationCategory.Notification, ClassifiedBy = "rule" },
            new Classification { MessageId = "fwd", Category = ClassificationCategory.Notification, ClassifiedBy = "rule" });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        var row = Assert.Single(data);
        Assert.Equal("normal", row.MessageId);
    }

    [Fact]
    public async Task BuildTrainingData_WhitelistDomain_ContributesKeepRows()
    {
        _db.Emails.AddRange(
            MakeEmail("p1", "alice@friends.com", "friends.com"),
            MakeEmail("p2", "bob@friends.com", "friends.com"));
        _db.Whitelist.Add(new WhitelistEntry { Pattern = "friends.com", PatternType = "domain" });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        Assert.Equal(2, data.Count);
        Assert.All(data, r => Assert.Equal(ClassificationCategory.Keep.ToString(), r.Label));
    }

    [Fact]
    public async Task BuildTrainingData_WhitelistEmail_ContributesKeepRow()
    {
        _db.Emails.Add(MakeEmail("p1", "alice@example.com", "example.com"));
        _db.Whitelist.Add(new WhitelistEntry { Pattern = "alice@example.com", PatternType = "email" });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        var row = Assert.Single(data);
        Assert.Equal(ClassificationCategory.Keep.ToString(), row.Label);
    }

    [Fact]
    public async Task BuildTrainingData_HumanKeepDecision_OverridesRuleLabel()
    {
        // The rule said Notification, but the user marked it Keep — the user wins.
        _db.Emails.Add(MakeEmail("m1", subject: "your password has been changed"));
        _db.Classifications.Add(new Classification
        {
            MessageId = "m1",
            Category = ClassificationCategory.Notification,
            ClassifiedBy = "rule",
            ReviewDecision = ReviewDecision.Keep,
        });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        var row = Assert.Single(data);
        Assert.Equal(ClassificationCategory.Keep.ToString(), row.Label);
    }

    [Fact]
    public async Task BuildTrainingData_HumanWhitelistDecision_OverridesRuleLabel()
    {
        _db.Emails.Add(MakeEmail("m1"));
        _db.Classifications.Add(new Classification
        {
            MessageId = "m1",
            Category = ClassificationCategory.Marketing,
            ClassifiedBy = "rule",
            ReviewDecision = ReviewDecision.Whitelisted,
        });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        var row = Assert.Single(data);
        Assert.Equal(ClassificationCategory.Keep.ToString(), row.Label);
    }

    [Fact]
    public async Task BuildTrainingData_PopulatesProviderAgnosticFeatures()
    {
        // No GmailCategory / no List-Unsubscribe — simulating a POP3/IMAP source.
        // The row should still be built; Gmail-specific fields just degrade to defaults.
        _db.Emails.Add(MakeEmail("m1",
            from: "support@vendor.io",
            subject: "Re: your invoice",
            snippet: "as discussed"));
        _db.Classifications.Add(new Classification
        {
            MessageId = "m1",
            Category = ClassificationCategory.Transactional,
            ClassifiedBy = "rule",
        });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        // Re: subject is a reply → excluded by the forward/reply guard.
        Assert.Empty(data);
    }

    [Fact]
    public async Task BuildTrainingData_BuildsExpectedFeatures()
    {
        _db.Emails.Add(MakeEmail("m1",
            from: "Joe <joe@vendor.io>",
            fromDomain: "vendor.io",
            subject: "Fwd: stuff",
            gmailCategory: "CATEGORY_PROMOTIONS",
            hasListUnsubscribe: true));
        // Use a Whitelist row so the email gets into training without being filtered
        // by the forward-guard (whitelist trumps everything for a Keep label).
        _db.Whitelist.Add(new WhitelistEntry { Pattern = "vendor.io", PatternType = "domain" });
        await _db.SaveChangesAsync();

        var data = await _trainer.BuildTrainingDataAsync(onStatus: null, CancellationToken.None);

        var row = Assert.Single(data);
        Assert.Equal("Joe <joe@vendor.io>", row.From);
        Assert.Equal("vendor.io", row.FromDomain);
        Assert.Equal("CATEGORY_PROMOTIONS", row.GmailCategory);
        Assert.Equal(1f, row.HasListUnsubscribe);
        Assert.Equal(1f, row.IsForwardedOrReply);
        Assert.Equal("stuff", row.CleanSubject);
        Assert.Equal("Fwd: stuff", row.Subject);
    }
}

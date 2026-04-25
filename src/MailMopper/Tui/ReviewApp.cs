using System.Globalization;
using MailMopper.Data;
using MailMopper.Models;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace MailMopper.Tui;

/// <summary>
/// Interactive review experience using Spectre.Console prompts and tables.
/// Provides a 3-level drill-down: Categories > Senders > Emails.
/// </summary>
public partial class ReviewApp
{
    private readonly AppDbContext _db;
    private List<Classification> _allReviewable = [];
    private List<ClassificationGroup> _groups = [];
    private bool _dirty;
    private int _unsavedActions;
    private int _previouslyTrashedCount;
    private long _previouslyTrashedSize;
    private int? _yearFilter;
    private List<int> _availableYears = [];

    private const int AutoSaveThreshold = 20;
    internal const int PageSize = 30;

    public ReviewApp(AppDbContext db) => _db = db;

    public async Task RunAsync(CancellationToken ct)
    {
        await LoadDataAsync(ct);

        if (_groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No classified emails to review. Run 'classify' first.[/]");
            return;
        }

        var running = true;
        while (running)
        {
            ct.ThrowIfCancellationRequested();
            running = await ShowDashboardAsync(ct);
        }

        if (_dirty)
        {
            await SaveAsync(ct);
            AnsiConsole.MarkupLine("[green]✓ Decisions saved.[/]");
        }
    }

    private async Task LoadDataAsync(CancellationToken ct)
    {
        var allClassifications = await _db.Classifications
            .Include(c => c.Email)
            .Where(c => c.Email != null)
            .ToListAsync(ct);

        var trashedMessageIds = await _db.Actions
            .Where(a => a.Action == "trash")
            .Select(a => a.MessageId)
            .Distinct()
            .ToListAsync(ct);
        var trashedSet = new HashSet<string>(trashedMessageIds);

        bool retroFixed = false;
        foreach (var c in allClassifications)
        {
            if (c.ReviewDecision != ReviewDecision.Executed && trashedSet.Contains(c.MessageId))
            {
                c.ReviewDecision = ReviewDecision.Executed;
                retroFixed = true;
            }
        }
        if (retroFixed)
            await _db.SaveChangesAsync(ct);

        var executed = allClassifications.Where(c => c.ReviewDecision == ReviewDecision.Executed).ToList();
        _previouslyTrashedCount = executed.Count;
        _previouslyTrashedSize = executed.Sum(c => c.Email?.SizeEstimate ?? 0);

        _allReviewable = allClassifications
            .Where(c => c.ReviewDecision != ReviewDecision.Executed)
            .ToList();

        _availableYears = _allReviewable
            .Where(c => c.Email?.Date != null)
            .Select(c => c.Email!.Date!.Value.Year)
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        RebuildGroups();
    }

    private void RebuildGroups()
    {
        var filtered = _yearFilter.HasValue
            ? _allReviewable.Where(c => c.Email?.Date?.Year == _yearFilter.Value).ToList()
            : _allReviewable;

        _groups = filtered
            .GroupBy(c => c.Category)
            .OrderByDescending(g => g.Count())
            .Select(g => new ClassificationGroup
            {
                Category = g.Key,
                Classifications = g.ToList(),
                Decision = ComputeGroupDecision(g)
            })
            .ToList();
    }

    private static ReviewDecision ComputeGroupDecision(IGrouping<ClassificationCategory, Classification> g)
    {
        if (g.All(c => c.ReviewDecision == ReviewDecision.ApproveTrash))
            return ReviewDecision.ApproveTrash;
        if (g.All(c => c.ReviewDecision == ReviewDecision.Keep))
            return ReviewDecision.Keep;
        if (g.All(c => c.ReviewDecision == ReviewDecision.Whitelisted))
            return ReviewDecision.Whitelisted;
        return ReviewDecision.Pending;
    }

    private async Task AutoSaveIfNeeded(CancellationToken ct)
    {
        if (_unsavedActions >= AutoSaveThreshold)
        {
            await _db.SaveChangesAsync(ct);
            _unsavedActions = 0;
        }
    }

    private void MarkDirty(int actionCount = 1)
    {
        _dirty = true;
        _unsavedActions += actionCount;
    }

    // ── Input helpers ─────────────────────────────────────────────────

    private static string ReadCommand(string prompt, string instantKeys, string? defaultValue = null)
    {
        AnsiConsole.Markup(prompt);

        var firstKey = Console.ReadKey(intercept: true);

        if (firstKey.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return defaultValue ?? "";
        }
        if (firstKey.Key == ConsoleKey.Escape)
        {
            Console.WriteLine();
            return "B";
        }

        char c = char.ToUpper(firstKey.KeyChar, CultureInfo.InvariantCulture);

        if (instantKeys.Contains(c))
        {
            Console.WriteLine();
            return c.ToString();
        }

        Console.Write(firstKey.KeyChar);
        var rest = Console.ReadLine() ?? "";
        return firstKey.KeyChar + rest;
    }

    private void PromptYearFilter()
    {
        var yearChoices = new List<string> { "All years" };
        yearChoices.AddRange(_availableYears.Select(y => y.ToString(CultureInfo.InvariantCulture)));
        var yearPick = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a year:")
                .PageSize(15)
                .AddChoices(yearChoices));

        if (yearPick == "All years")
            _yearFilter = null;
        else if (int.TryParse(yearPick, out var y))
            _yearFilter = y;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        _unsavedActions = 0;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };

    private static string FormatDecision(ReviewDecision d) => d switch
    {
        ReviewDecision.ApproveTrash => "[red]Trash[/]",
        ReviewDecision.Keep => "[green]Keep[/]",
        ReviewDecision.Whitelisted => "[cyan]Whitelisted[/]",
        _ => "[yellow]Pending[/]"
    };

    private static List<SenderGroup> BuildSenderList(ClassificationGroup group)
    {
        return group.Classifications
            .GroupBy(c => new { Domain = c.Email?.FromDomain ?? "unknown", Email = c.Email?.From ?? "unknown" })
            .OrderByDescending(g => g.Count())
            .Select(g => new SenderGroup
            {
                From = g.Key.Email,
                Domain = g.Key.Domain,
                Classifications = g.ToList(),
                Decision = g.All(c => c.ReviewDecision == ReviewDecision.ApproveTrash) ? ReviewDecision.ApproveTrash
                         : g.All(c => c.ReviewDecision == ReviewDecision.Keep) ? ReviewDecision.Keep
                         : g.All(c => c.ReviewDecision == ReviewDecision.Whitelisted) ? ReviewDecision.Whitelisted
                         : ReviewDecision.Pending
            })
            .ToList();
    }

    private static int FindNextPendingIndex(List<SenderGroup> senders, int currentIndex)
    {
        for (int i = currentIndex + 1; i < senders.Count; i++)
            if (senders[i].Decision == ReviewDecision.Pending)
                return i;
        for (int i = 0; i < currentIndex; i++)
            if (senders[i].Decision == ReviewDecision.Pending)
                return i;
        return -1;
    }

    private void RenderYearBreakdown()
    {
        var yearBreakdown = _allReviewable
            .Where(c => c.Email?.Date != null)
            .GroupBy(c => c.Email!.Date!.Value.Year)
            .OrderBy(g => g.Key)
            .Select(g => new { Year = g.Key, Count = g.Count(), Size = g.Sum(c => c.Email?.SizeEstimate ?? 0) })
            .ToList();

        if (yearBreakdown.Count <= 1)
            return;

        var yearTable = new Table().Border(TableBorder.Minimal).Expand();
        yearTable.AddColumn("[bold]Year[/]");
        foreach (var yb in yearBreakdown)
        {
            var highlight = _yearFilter == yb.Year ? "[bold cyan]" : "[dim]";
            yearTable.AddColumn(new TableColumn($"{highlight}{yb.Year}[/]").RightAligned());
        }
        var emailsRow = new List<string> { "[bold]Emails[/]" }
                .Concat(yearBreakdown.Select(yb =>
                {
                    var highlight = _yearFilter == yb.Year ? "[bold cyan]" : "[dim]";
                    return $"{highlight}{yb.Count:N0}[/]";
                }))
                .ToArray();
        yearTable.AddRow(emailsRow);
        var sizeRow = new List<string> { "[bold]Size[/]" }
                .Concat(yearBreakdown.Select(yb =>
                {
                    var highlight = _yearFilter == yb.Year ? "[bold cyan]" : "[dim]";
                    return $"{highlight}{FormatSize(yb.Size)}[/]";
                }))
                .ToArray();
        yearTable.AddRow(sizeRow);
        AnsiConsole.Write(yearTable);
        AnsiConsole.WriteLine();
    }

    private class ClassificationGroup
    {
        public ClassificationCategory Category { get; set; }
        public List<Classification> Classifications { get; set; } = [];
        public ReviewDecision Decision { get; set; }
    }

    private class SenderGroup
    {
        public string From { get; set; } = "";
        public string Domain { get; set; } = "";
        public List<Classification> Classifications { get; set; } = [];
        public ReviewDecision Decision { get; set; }
    }
}

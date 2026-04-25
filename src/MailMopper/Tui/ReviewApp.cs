using System.Globalization;
using MailMopper.Data;
using MailMopper.Models;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace MailMopper.Tui;

/// <summary>
/// Interactive review experience using Spectre.Console prompts and tables.
/// Provides a 3-level drill-down: Categories > Senders > Emails.
/// Optimised for rapid bulk review with keyboard shortcuts and auto-advance.
/// </summary>
public class ReviewApp
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
    private const int PageSize = 30;

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

        // Retroactively mark classifications that were trashed before the Executed status existed
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

    // ── Dashboard ──────────────────────────────────────────────────────

    private async Task<bool> ShowDashboardAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold blue]Gmail Cleanup — Review Dashboard[/]").LeftJustified());

        var yearLabel = _yearFilter.HasValue ? $"[bold cyan]{_yearFilter.Value}[/]" : "[dim]All years[/]";
        AnsiConsole.MarkupLine($"  Year filter: {yearLabel}");
        AnsiConsole.WriteLine();

        // Year breakdown
        RenderYearBreakdown();

        // Category table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]#[/]").RightAligned())
            .AddColumn("[bold]Category[/]")
            .AddColumn(new TableColumn("[bold]Emails[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Senders[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Size[/]").RightAligned())
            .AddColumn("[bold]Top Domain[/]")
            .AddColumn("[bold]Progress[/]");

        for (int i = 0; i < _groups.Count; i++)
        {
            var g = _groups[i];
            var count = g.Classifications.Count;
            var senderCount = g.Classifications.Select(c => c.Email?.From).Distinct().Count();
            var size = FormatSize(g.Classifications.Sum(c => c.Email?.SizeEstimate ?? 0));
            var topDomain = g.Classifications
                .GroupBy(c => c.Email?.FromDomain ?? "unknown")
                .OrderByDescending(sg => sg.Count())
                .FirstOrDefault()?.Key ?? "-";

            var decided = g.Classifications.Count(c => c.ReviewDecision != ReviewDecision.Pending);
            var progressText = decided == count
                ? g.Decision switch
                {
                    ReviewDecision.ApproveTrash => "[red]✗ Trash[/]",
                    ReviewDecision.Keep => "[green]✓ Keep[/]",
                    ReviewDecision.Whitelisted => "[cyan]✓ Whitelisted[/]",
                    _ => "[green]✓ Done[/]"
                }
                : decided > 0
                    ? $"[yellow]{decided}/{count} reviewed[/]"
                    : "[dim]Not started[/]";

            table.AddRow(
                $"[bold]{i + 1}[/]",
                $"[bold]{g.Category}[/]",
                count.ToString("N0", CultureInfo.InvariantCulture),
                senderCount.ToString("N0", CultureInfo.InvariantCulture),
                size,
                Markup.Escape(topDomain.Length > 25 ? topDomain[..22] + "..." : topDomain),
                progressText);
        }

        AnsiConsole.Write(table);

        // Summary
        var totalRemaining = _groups.Sum(g => g.Classifications.Count);
        var totalRemainingSize = _groups.Sum(g => g.Classifications.Sum(c => c.Email?.SizeEstimate ?? 0));
        var totalPendingEmails = _groups.Sum(g => g.Classifications.Count(c => c.ReviewDecision == ReviewDecision.Pending));
        var totalTrashEmails = _groups.Sum(g => g.Classifications.Count(c => c.ReviewDecision == ReviewDecision.ApproveTrash));
        var totalKeepEmails = _groups.Sum(g => g.Classifications.Count(c => c.ReviewDecision == ReviewDecision.Keep));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]Total:[/] {totalRemaining:N0} emails ({FormatSize(totalRemainingSize)})");
        if (_previouslyTrashedCount > 0)
            AnsiConsole.MarkupLine($"  [dim]Already executed:[/] {_previouslyTrashedCount:N0} emails ({FormatSize(_previouslyTrashedSize)})");
        AnsiConsole.MarkupLine($"  [yellow]Pending:[/] {totalPendingEmails:N0}  [red]Trash:[/] {totalTrashEmails:N0}  [green]Keep:[/] {totalKeepEmails:N0}");
        if (_dirty)
            AnsiConsole.MarkupLine($"  [dim italic]Unsaved changes ({_unsavedActions} actions)[/]");
        AnsiConsole.WriteLine();

        // Input
        var navHints = new List<string>
        {
            "[blue]#[/]=open category",
            "[cyan]Y[/]=year filter",
            "[bold]S[/]=save & exit",
            "[dim]Q[/]=quit"
        };
        AnsiConsole.MarkupLine($"  {string.Join("  │  ", navHints)}");

        var input = ReadCommand("[blue]Dashboard: [/]", "SQY");

        if (string.IsNullOrWhiteSpace(input))
            return true;

        if (input.Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            if (_dirty)
            {
                await SaveAsync(ct);
                AnsiConsole.MarkupLine("[green]✓ Saved.[/]");
            }
            return false;
        }
        if (input.Equals("Q", StringComparison.OrdinalIgnoreCase))
        {
            if (_dirty)
            {
                if (AnsiConsole.Confirm("[yellow]You have unsaved changes. Save before quitting?[/]", defaultValue: true))
                {
                    await SaveAsync(ct);
                    AnsiConsole.MarkupLine("[green]✓ Saved.[/]");
                }
            }
            return false;
        }
        if (input.Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            PromptYearFilter();
            RebuildGroups();
            return true;
        }
        if (int.TryParse(input, out var catNum) && catNum >= 1 && catNum <= _groups.Count)
        {
            await ShowCategoryAsync(_groups[catNum - 1], ct);
            return true;
        }

        return true;
    }

    // ── Category (sender list) ─────────────────────────────────────────

    private async Task ShowCategoryAsync(ClassificationGroup group, CancellationToken ct)
    {
        int lastSelectedIndex = -1;
        int currentPage = 0;
        bool hidingDecided = true;

        var inCategory = true;
        while (inCategory)
        {
            AnsiConsole.Clear();

            // Build sender list
            var allSenders = BuildSenderList(group);

            var filteredSenders = hidingDecided
                ? allSenders.Where(s => s.Decision == ReviewDecision.Pending).ToList()
                : allSenders;

            int totalPending = allSenders.Count(s => s.Decision == ReviewDecision.Pending);
            int totalDecided = allSenders.Count - totalPending;
            int totalFiltered = filteredSenders.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalFiltered / PageSize));

            if (currentPage >= totalPages)
                currentPage = totalPages - 1;
            if (currentPage < 0)
                currentPage = 0;

            int startIdx = currentPage * PageSize;
            int endIdx = Math.Min(startIdx + PageSize, totalFiltered);
            var pageSenders = filteredSenders.Skip(startIdx).Take(PageSize).ToList();

            // Header
            var yearInfo = _yearFilter.HasValue ? $" [cyan]({_yearFilter.Value})[/]" : "";
            AnsiConsole.Write(new Rule($"[bold blue]{group.Category}[/]{yearInfo} — {group.Classifications.Count:N0} emails, {allSenders.Count} senders").LeftJustified());

            // Progress bar
            var pct = allSenders.Count > 0 ? totalDecided * 100 / allSenders.Count : 100;
            var bar = new string('█', pct / 5) + new string('░', 20 - pct / 5);
            AnsiConsole.MarkupLine($"  [green]{bar}[/] {totalDecided}/{allSenders.Count} senders reviewed ({pct}%)");

            if (totalFiltered == 0)
            {
                AnsiConsole.WriteLine();
                if (hidingDecided && totalDecided > 0)
                    AnsiConsole.MarkupLine("  [green]All senders in this category have been reviewed![/] Press [blue]H[/] to show all, or [yellow]B[/] to go back.");
                else
                    AnsiConsole.MarkupLine("  [dim]No senders to display.[/]");
                AnsiConsole.WriteLine();
            }
            else
            {
                var filterLabel = hidingDecided ? "[green]pending only[/]" : "[dim]all senders[/]";
                AnsiConsole.MarkupLine($"  Showing {startIdx + 1}–{endIdx} of {totalFiltered} senders ({filterLabel})");
                if (totalPages > 1)
                    AnsiConsole.MarkupLine($"  Page [bold]{currentPage + 1}[/] of {totalPages}");
                AnsiConsole.WriteLine();

                // Sender table
                var senderTable = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn(new TableColumn("[bold]#[/]").RightAligned())
                    .AddColumn("[bold]Sender[/]")
                    .AddColumn(new TableColumn("[bold]Emails[/]").RightAligned())
                    .AddColumn("[bold]Domain[/]")
                    .AddColumn("[bold]Status[/]");

                for (int i = 0; i < pageSenders.Count; i++)
                {
                    var s = pageSenders[i];
                    var status = FormatDecision(s.Decision);
                    var from = s.From.Length > 40 ? s.From[..37] + "..." : s.From;
                    var displayNum = startIdx + i + 1;
                    var marker = (startIdx + i) == lastSelectedIndex ? "[bold cyan]›[/]" : " ";
                    senderTable.AddRow(
                        $"{marker}{displayNum}",
                        Markup.Escape(from),
                        s.Classifications.Count.ToString("N0", CultureInfo.InvariantCulture),
                        Markup.Escape(s.Domain),
                        status);
                }

                AnsiConsole.Write(senderTable);
                AnsiConsole.WriteLine();
            }

            // Command legend — always show
            var navHints = new List<string> { "[blue]#[/]=view" };
            navHints.Add("[red]T#[/]=trash");
            navHints.Add("[green]K#[/]=keep");
            if (currentPage > 0)
                navHints.Add("[blue]P[/]=prev");
            if (currentPage < totalPages - 1)
                navHints.Add("[blue]N[/]=next");
            navHints.Add(hidingDecided ? "[blue]H[/]=show all" : "[blue]H[/]=pending only");
            navHints.Add("[cyan]Y[/]=year");
            navHints.Add("[red]TA[/]=trash all");
            navHints.Add("[green]KA[/]=keep all");
            navHints.Add("[yellow]B[/]=back");

            AnsiConsole.MarkupLine($"  [dim]{string.Join(" │ ", navHints)}[/]");

            var defaultAction = lastSelectedIndex >= 0 && lastSelectedIndex >= startIdx && lastSelectedIndex < endIdx
                ? (lastSelectedIndex + 1).ToString(CultureInfo.InvariantCulture) : "";
            var defaultHint = defaultAction != "" ? $" (Enter=#{defaultAction})" : "";
            var input = ReadCommand($"[blue]Choice{defaultHint}: [/]", "BHNPY", defaultAction);

            var trimmed = input.Trim();

            if (trimmed.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                inCategory = false;
            }
            else if (trimmed.Equals("N", StringComparison.OrdinalIgnoreCase))
            {
                if (currentPage < totalPages - 1)
                    currentPage++;
            }
            else if (trimmed.Equals("P", StringComparison.OrdinalIgnoreCase))
            {
                if (currentPage > 0)
                    currentPage--;
            }
            else if (trimmed.Equals("H", StringComparison.OrdinalIgnoreCase))
            {
                hidingDecided = !hidingDecided;
                currentPage = 0;
                lastSelectedIndex = -1;
            }
            else if (trimmed.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                PromptYearFilter();
                RebuildGroups();

                var updatedGroup = _groups.FirstOrDefault(g => g.Category == group.Category);
                if (updatedGroup == null || updatedGroup.Classifications.Count == 0)
                {
                    inCategory = false;
                }
                else
                {
                    group = updatedGroup;
                    currentPage = 0;
                    lastSelectedIndex = -1;
                }
            }
            else if (trimmed.Equals("TA", StringComparison.OrdinalIgnoreCase))
            {
                var pending = group.Classifications.Where(c => c.ReviewDecision == ReviewDecision.Pending).ToList();
                if (pending.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No pending emails to trash.[/]");
                    await Task.Delay(500, ct);
                }
                else
                {
                    foreach (var c in pending)
                        c.ReviewDecision = ReviewDecision.ApproveTrash;
                    MarkDirty(pending.Count);
                    await AutoSaveIfNeeded(ct);
                    AnsiConsole.MarkupLine($"[red]✗ Marked {pending.Count:N0} pending emails for trash.[/]");
                    await Task.Delay(400, ct);
                }
            }
            else if (trimmed.Equals("KA", StringComparison.OrdinalIgnoreCase))
            {
                var pending = group.Classifications.Where(c => c.ReviewDecision == ReviewDecision.Pending).ToList();
                if (pending.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No pending emails to keep.[/]");
                    await Task.Delay(500, ct);
                }
                else
                {
                    foreach (var c in pending)
                        c.ReviewDecision = ReviewDecision.Keep;
                    MarkDirty(pending.Count);
                    await AutoSaveIfNeeded(ct);
                    AnsiConsole.MarkupLine($"[green]✓ Marked {pending.Count:N0} pending emails to keep.[/]");
                    await Task.Delay(400, ct);
                }
            }
            // Quick-action: T# or K# (e.g. T5, K12)
            else if (trimmed.Length >= 2
                     && (trimmed[0] == 'T' || trimmed[0] == 't' || trimmed[0] == 'K' || trimmed[0] == 'k')
                     && int.TryParse(trimmed[1..], out var quickNum)
                     && quickNum >= 1 && quickNum <= totalFiltered)
            {
                var target = filteredSenders[quickNum - 1];
                var decision = char.ToUpper(trimmed[0], CultureInfo.InvariantCulture) == 'T'
                    ? ReviewDecision.ApproveTrash
                    : ReviewDecision.Keep;

                foreach (var c in target.Classifications)
                    c.ReviewDecision = decision;
                target.Decision = decision;
                MarkDirty(target.Classifications.Count);
                await AutoSaveIfNeeded(ct);

                var label = decision == ReviewDecision.ApproveTrash ? "[red]trash[/]" : "[green]keep[/]";
                AnsiConsole.MarkupLine($"  → {Markup.Escape(target.From)}: {label} ({target.Classifications.Count} emails)");

                // Auto-advance: set cursor to next pending sender
                lastSelectedIndex = FindNextPendingIndex(filteredSenders, quickNum - 1);
                currentPage = lastSelectedIndex >= 0 ? lastSelectedIndex / PageSize : currentPage;
            }
            else if (int.TryParse(trimmed, out var num) && num >= 1 && num <= totalFiltered)
            {
                lastSelectedIndex = num - 1;
                var selectedSender = filteredSenders[lastSelectedIndex];
                var result = await ShowSenderAsync(selectedSender, ct);

                if (result != SenderAction.Back)
                {
                    await AutoSaveIfNeeded(ct);

                    // Auto-advance to next pending sender
                    lastSelectedIndex = FindNextPendingIndex(filteredSenders, lastSelectedIndex);
                    currentPage = lastSelectedIndex >= 0 ? lastSelectedIndex / PageSize : currentPage;
                }
                else
                {
                    currentPage = lastSelectedIndex / PageSize;
                }
            }
        }

        // Refresh group decision after changes
        var updatedGrp = _groups.FirstOrDefault(g => g.Category == group.Category);
        if (updatedGrp != null)
        {
            updatedGrp.Decision = updatedGrp.Classifications.All(c => c.ReviewDecision == ReviewDecision.ApproveTrash) ? ReviewDecision.ApproveTrash
                : updatedGrp.Classifications.All(c => c.ReviewDecision == ReviewDecision.Keep) ? ReviewDecision.Keep
                : updatedGrp.Classifications.All(c => c.ReviewDecision == ReviewDecision.Whitelisted) ? ReviewDecision.Whitelisted
                : ReviewDecision.Pending;
        }
    }

    // ── Sender detail ──────────────────────────────────────────────────

    private enum SenderAction { Back, Decided }

    private const int SenderPageSize = 25;

    private async Task<SenderAction> ShowSenderAsync(SenderGroup sender, CancellationToken ct)
    {
        var ordered = sender.Classifications.OrderByDescending(c => c.Email?.Date).ToList();
        int total = ordered.Count;
        int page = 0;
        int totalPages = (int)Math.Ceiling((double)total / SenderPageSize);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule($"[bold blue]{Markup.Escape(sender.From)}[/] — {total} emails").LeftJustified());
            AnsiConsole.MarkupLine($"  Domain: [cyan]{Markup.Escape(sender.Domain)}[/]");
            AnsiConsole.WriteLine();

            var emailTable = new Table()
                .Border(TableBorder.Simple)
                .AddColumn("[bold]Date[/]")
                .AddColumn("[bold]Subject[/]")
                .AddColumn(new TableColumn("[bold]Size[/]").RightAligned());

            var pageSlice = ordered.Skip(page * SenderPageSize).Take(SenderPageSize);
            foreach (var c in pageSlice)
            {
                var date = c.Email?.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
                var subject = c.Email?.Subject ?? "-";
                subject = subject.Length > 70 ? subject[..67] + "..." : subject;
                var size = FormatSize(c.Email?.SizeEstimate ?? 0);
                emailTable.AddRow(date, Markup.Escape(subject), size);
            }

            if (totalPages > 1)
                emailTable.Caption($"[dim]Page {page + 1}/{totalPages} — {total} emails[/]");

            AnsiConsole.Write(emailTable);
            AnsiConsole.WriteLine();

            var hints = new List<string> { "[red]T[/]=trash", "[green]K[/]=keep", "[cyan]W[/]=whitelist domain", "[yellow]B[/]=back" };
            if (page > 0)
                hints.Add("[dim]←[/]=prev");
            if (page < totalPages - 1)
                hints.Add("[dim]→[/]=next");
            AnsiConsole.MarkupLine($"  {string.Join("  ", hints)}");

            AnsiConsole.Markup("[blue]Action: [/]");
            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();

            var action = char.ToUpper(key.KeyChar, CultureInfo.InvariantCulture);

            if (key.Key == ConsoleKey.LeftArrow && page > 0)
            {
                page--;
                continue;
            }
            if (key.Key == ConsoleKey.RightArrow && page < totalPages - 1)
            {
                page++;
                continue;
            }

            if (action == 'T')
            {
                foreach (var c in sender.Classifications)
                    c.ReviewDecision = ReviewDecision.ApproveTrash;
                sender.Decision = ReviewDecision.ApproveTrash;
                MarkDirty(sender.Classifications.Count);
                return SenderAction.Decided;
            }
            if (action == 'K')
            {
                foreach (var c in sender.Classifications)
                    c.ReviewDecision = ReviewDecision.Keep;
                sender.Decision = ReviewDecision.Keep;
                MarkDirty(sender.Classifications.Count);
                return SenderAction.Decided;
            }
            if (action == 'W')
            {
                var existing = await _db.Whitelist
                    .AnyAsync(w => w.Pattern == sender.Domain, ct);
                if (!existing)
                {
                    _db.Whitelist.Add(new WhitelistEntry
                    {
                        Pattern = sender.Domain,
                        PatternType = "domain",
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    await _db.SaveChangesAsync(ct);
                }
                foreach (var c in sender.Classifications)
                    c.ReviewDecision = ReviewDecision.Whitelisted;
                sender.Decision = ReviewDecision.Whitelisted;
                MarkDirty(sender.Classifications.Count);
                return SenderAction.Decided;
            }

            return SenderAction.Back;
        }
    }

    // ── Input helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Reads a command using instant single-key detection.
    /// Keys in instantKeys execute immediately (no Enter needed).
    /// Other keys echo and fall through to line-read for compound commands.
    /// Enter alone returns the defaultValue.
    /// </summary>
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

        // Instant single-key actions
        if (instantKeys.Contains(c))
        {
            Console.WriteLine();
            return c.ToString();
        }

        // Compound command — echo first char and read the rest with Enter
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
        // Look forward from current position
        for (int i = currentIndex + 1; i < senders.Count; i++)
            if (senders[i].Decision == ReviewDecision.Pending)
                return i;
        // Wrap around to beginning
        for (int i = 0; i < currentIndex; i++)
            if (senders[i].Decision == ReviewDecision.Pending)
                return i;
        return -1;
    }

    private static string FormatDecision(ReviewDecision d) => d switch
    {
        ReviewDecision.ApproveTrash => "[red]Trash[/]",
        ReviewDecision.Keep => "[green]Keep[/]",
        ReviewDecision.Whitelisted => "[cyan]Whitelisted[/]",
        _ => "[yellow]Pending[/]"
    };

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

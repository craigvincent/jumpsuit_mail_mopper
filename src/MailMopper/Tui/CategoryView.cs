using System.Globalization;
using MailMopper.Models;
using Spectre.Console;

namespace MailMopper.Tui;

/// <summary>
/// Category drill-down view — lists senders within a category with bulk actions.
/// </summary>
public partial class ReviewApp
{
    private sealed class CategoryViewState
    {
        public ClassificationGroup Group { get; set; } = null!;
        public int CurrentPage { get; set; }
        public int LastSelectedIndex { get; set; } = -1;
        public bool HidingDecided { get; set; } = true;
        public bool Running { get; set; } = true;
    }

    private async Task ShowCategoryAsync(ClassificationGroup group, CancellationToken ct)
    {
        var state = new CategoryViewState { Group = group };

        while (state.Running)
        {
            AnsiConsole.Clear();
            var (filteredSenders, totalPages, startIdx, pageSenders) = BuildCategoryPageData(state);
            RenderCategoryPage(state, filteredSenders, totalPages, startIdx, pageSenders);
            var input = ReadCategoryInput(state, startIdx);
            await ProcessCategoryInputAsync(state, input, filteredSenders, totalPages, ct);
        }

        UpdateGroupDecision(state.Group);
    }

    private static (List<SenderGroup> filteredSenders, int totalPages, int startIdx, List<SenderGroup> pageSenders)
        BuildCategoryPageData(CategoryViewState state)
    {
        var allSenders = BuildSenderList(state.Group);
        var filteredSenders = state.HidingDecided
            ? allSenders.Where(s => s.Decision == ReviewDecision.Pending).ToList()
            : allSenders;

        var totalFiltered = filteredSenders.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalFiltered / PageSize));
        state.CurrentPage = Math.Clamp(state.CurrentPage, 0, totalPages - 1);

        var startIdx = state.CurrentPage * PageSize;
        var pageSenders = filteredSenders.Skip(startIdx).Take(PageSize).ToList();

        return (filteredSenders, totalPages, startIdx, pageSenders);
    }

    private void RenderCategoryPage(
        CategoryViewState state,
        IList<SenderGroup> filteredSenders,
        int totalPages,
        int startIdx,
        IList<SenderGroup> pageSenders)
    {
        var allSenders = BuildSenderList(state.Group);
        var totalPending = allSenders.Count(s => s.Decision == ReviewDecision.Pending);
        var totalDecided = allSenders.Count - totalPending;
        var totalFiltered = filteredSenders.Count;
        var endIdx = Math.Min(startIdx + PageSize, totalFiltered);

        var yearInfo = _yearFilter.HasValue ? $" [cyan]({_yearFilter.Value})[/]" : "";
        AnsiConsole.Write(new Rule($"[bold blue]{state.Group.Category}[/]{yearInfo} — {state.Group.Classifications.Count:N0} emails, {allSenders.Count} senders").LeftJustified());

        var pct = allSenders.Count > 0 ? totalDecided * 100 / allSenders.Count : 100;
        var bar = new string('█', pct / 5) + new string('░', 20 - pct / 5);
        AnsiConsole.MarkupLine($"  [green]{bar}[/] {totalDecided}/{allSenders.Count} senders reviewed ({pct}%)");

        if (totalFiltered == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(state.HidingDecided && totalDecided > 0
                ? "  [green]All senders in this category have been reviewed![/] Press [blue]H[/] to show all, or [yellow]B[/] to go back."
                : "  [dim]No senders to display.[/]");
            AnsiConsole.WriteLine();
        }
        else
        {
            RenderSenderTable(state, pageSenders, startIdx, endIdx, totalPages, totalFiltered);
        }

        RenderCategoryNavHints(state, totalPages);
    }

    private static void RenderSenderTable(
        CategoryViewState state,
        IList<SenderGroup> pageSenders,
        int startIdx,
        int endIdx,
        int totalPages,
        int totalFiltered)
    {
        var filterLabel = state.HidingDecided ? "[green]pending only[/]" : "[dim]all senders[/]";
        AnsiConsole.MarkupLine($"  Showing {startIdx + 1}–{endIdx} of {totalFiltered} senders ({filterLabel})");
        if (totalPages > 1)
            AnsiConsole.MarkupLine($"  Page [bold]{state.CurrentPage + 1}[/] of {totalPages}");
        AnsiConsole.WriteLine();

        var senderTable = new Table()
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("[bold]#[/]").RightAligned())
            .AddColumn("[bold]Sender[/]")
            .AddColumn(new TableColumn("[bold]Emails[/]").RightAligned())
            .AddColumn("[bold]Domain[/]")
            .AddColumn("[bold]Status[/]");

        for (var i = 0; i < pageSenders.Count; i++)
        {
            var s = pageSenders[i];
            var status = FormatDecision(s.Decision);
            var from = s.From.Length > 40 ? s.From[..37] + "..." : s.From;
            var displayNum = startIdx + i + 1;
            var marker = (startIdx + i) == state.LastSelectedIndex ? "[bold cyan]›[/]" : " ";
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

    private static void RenderCategoryNavHints(CategoryViewState state, int totalPages)
    {
        var navHints = new List<string> { "[blue]#[/]=view", "[red]T#[/]=trash", "[green]K#[/]=keep" };
        if (state.CurrentPage > 0)
            navHints.Add("[blue]P[/]=prev");
        if (state.CurrentPage < totalPages - 1)
            navHints.Add("[blue]N[/]=next");
        navHints.Add(state.HidingDecided ? "[blue]H[/]=show all" : "[blue]H[/]=pending only");
        navHints.Add("[cyan]Y[/]=year");
        navHints.Add("[red]TA[/]=trash all");
        navHints.Add("[green]KA[/]=keep all");
        navHints.Add("[yellow]B[/]=back");
        AnsiConsole.MarkupLine($"  [dim]{string.Join(" │ ", navHints)}[/]");
    }

    private static string ReadCategoryInput(CategoryViewState state, int startIdx)
    {
        var defaultAction = state.LastSelectedIndex >= 0
                            && state.LastSelectedIndex >= startIdx
                            && state.LastSelectedIndex < startIdx + PageSize
            ? (state.LastSelectedIndex + 1).ToString(CultureInfo.InvariantCulture)
            : "";
        var defaultHint = defaultAction != "" ? $" (Enter=#{defaultAction})" : "";
        return ReadCommand($"[blue]Choice{defaultHint}: [/]", "BHNPY", defaultAction);
    }

    private async Task ProcessCategoryInputAsync(
        CategoryViewState state,
        string input,
        List<SenderGroup> filteredSenders,
        int totalPages,
        CancellationToken ct)
    {
        var trimmed = input.Trim();

        if (trimmed.Equals("B", StringComparison.OrdinalIgnoreCase))
        {
            state.Running = false;
            return;
        }
        if (trimmed.Equals("N", StringComparison.OrdinalIgnoreCase))
        {
            if (state.CurrentPage < totalPages - 1)
                state.CurrentPage++;
            return;
        }
        if (trimmed.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            if (state.CurrentPage > 0)
                state.CurrentPage--;
            return;
        }
        if (trimmed.Equals("H", StringComparison.OrdinalIgnoreCase))
        {
            state.HidingDecided = !state.HidingDecided;
            state.CurrentPage = 0;
            state.LastSelectedIndex = -1;
            return;
        }
        if (trimmed.Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            HandleYearFilter(state);
            return;
        }
        if (trimmed.Equals("TA", StringComparison.OrdinalIgnoreCase))
        {
            await HandleBulkDecisionAsync(state, ReviewDecision.ApproveTrash, "[red]✗ Marked {0:N0} pending emails for trash.[/]", ct);
            return;
        }
        if (trimmed.Equals("KA", StringComparison.OrdinalIgnoreCase))
        {
            await HandleBulkDecisionAsync(state, ReviewDecision.Keep, "[green]✓ Marked {0:N0} pending emails to keep.[/]", ct);
            return;
        }
        if (TryHandleQuickDecision(state, trimmed, filteredSenders))
        {
            await AutoSaveIfNeeded(ct);
            return;
        }
        if (int.TryParse(trimmed, out var num) && num >= 1 && num <= filteredSenders.Count)
            await HandleSenderViewAsync(state, num - 1, filteredSenders, ct);
    }

    private void HandleYearFilter(CategoryViewState state)
    {
        PromptYearFilter();
        RebuildGroups();

        var updatedGroup = _groups.FirstOrDefault(g => g.Category == state.Group.Category);
        if (updatedGroup == null || updatedGroup.Classifications.Count == 0)
        {
            state.Running = false;
        }
        else
        {
            state.Group = updatedGroup;
            state.CurrentPage = 0;
            state.LastSelectedIndex = -1;
        }
    }

    private async Task HandleBulkDecisionAsync(
        CategoryViewState state,
        ReviewDecision decision,
        string messageFormat,
        CancellationToken ct)
    {
        var pending = state.Group.Classifications.Where(c => c.ReviewDecision == ReviewDecision.Pending).ToList();
        if (pending.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No pending emails.[/]");
            await Task.Delay(500, ct);
            return;
        }
        foreach (var c in pending)
            c.ReviewDecision = decision;
        MarkDirty(pending.Count);
        await AutoSaveIfNeeded(ct);
        AnsiConsole.MarkupLine(string.Format(CultureInfo.InvariantCulture, messageFormat, pending.Count));
        await Task.Delay(400, ct);
    }

    private bool TryHandleQuickDecision(CategoryViewState state, string trimmed, List<SenderGroup> filteredSenders)
    {
        if (trimmed.Length < 2)
            return false;
        var cmd = char.ToUpper(trimmed[0], CultureInfo.InvariantCulture);
        if (cmd is not 'T' and not 'K')
            return false;
        if (!int.TryParse(trimmed[1..], out var quickNum))
            return false;
        if (quickNum < 1 || quickNum > filteredSenders.Count)
            return false;

        var target = filteredSenders[quickNum - 1];
        var decision = cmd == 'T' ? ReviewDecision.ApproveTrash : ReviewDecision.Keep;
        foreach (var c in target.Classifications)
            c.ReviewDecision = decision;
        target.Decision = decision;
        MarkDirty(target.Classifications.Count);

        var label = decision == ReviewDecision.ApproveTrash ? "[red]trash[/]" : "[green]keep[/]";
        AnsiConsole.MarkupLine($"  → {Markup.Escape(target.From)}: {label} ({target.Classifications.Count} emails)");

        state.LastSelectedIndex = FindNextPendingIndex(filteredSenders, quickNum - 1);
        state.CurrentPage = state.LastSelectedIndex >= 0 ? state.LastSelectedIndex / PageSize : state.CurrentPage;
        return true;
    }

    private async Task HandleSenderViewAsync(
        CategoryViewState state,
        int senderIndex,
        List<SenderGroup> filteredSenders,
        CancellationToken ct)
    {
        state.LastSelectedIndex = senderIndex;
        var result = await ShowSenderAsync(filteredSenders[senderIndex], ct);

        if (result != SenderAction.Back)
        {
            await AutoSaveIfNeeded(ct);
            state.LastSelectedIndex = FindNextPendingIndex(filteredSenders, senderIndex);
            state.CurrentPage = state.LastSelectedIndex >= 0 ? state.LastSelectedIndex / PageSize : state.CurrentPage;
        }
        else
        {
            state.CurrentPage = senderIndex / PageSize;
        }
    }

    private static void UpdateGroupDecision(ClassificationGroup group)
    {
        group.Decision = group.Classifications switch
        {
            var c when c.All(x => x.ReviewDecision == ReviewDecision.ApproveTrash) => ReviewDecision.ApproveTrash,
            var c when c.All(x => x.ReviewDecision == ReviewDecision.Keep) => ReviewDecision.Keep,
            var c when c.All(x => x.ReviewDecision == ReviewDecision.Whitelisted) => ReviewDecision.Whitelisted,
            _ => ReviewDecision.Pending,
        };
    }
}

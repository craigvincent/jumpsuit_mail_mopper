using MailMopper.Config;
using MailMopper.Data;
using MailMopper.Services;
using MailMopper.Tui.Views;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace MailMopper.Tui;

public enum AppTab { Home, Fetch, Classify, Review, Execute, Undo }

public sealed class MailMopperApp
{
    private readonly GmailSession _session;
    private readonly AppCancellation _cancellation;
    private readonly AppDbContext _db;
    private readonly AppSettings _settings;
    private readonly IAppView[] _views;

    private AppTab _currentTab = AppTab.Home;
    private bool _running = true;
    private bool _showHelp;

    private static readonly (AppTab Tab, string Label, string Hotkey)[] _tabs =
    [
        (AppTab.Home, "Home", "1"),
        (AppTab.Fetch, "Fetch", "2"),
        (AppTab.Classify, "Classify", "3"),
        (AppTab.Review, "Review", "4"),
        (AppTab.Execute, "Execute", "5"),
        (AppTab.Undo, "Undo", "6"),
    ];

    public MailMopperApp(
        GmailSession session,
        AppCancellation cancellation,
        AppDbContext db,
        AppSettings settings,
        HomeView homeView,
        FetchView fetchView,
        ClassifyView classifyView,
        ExecuteView executeView,
        UndoView undoView,
        ReviewView reviewView)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _views = new IAppView[(int)AppTab.Undo + 1];
        _views[(int)AppTab.Home] = homeView ?? throw new ArgumentNullException(nameof(homeView));
        _views[(int)AppTab.Fetch] = fetchView ?? throw new ArgumentNullException(nameof(fetchView));
        _views[(int)AppTab.Classify] = classifyView ?? throw new ArgumentNullException(nameof(classifyView));
        _views[(int)AppTab.Review] = reviewView ?? throw new ArgumentNullException(nameof(reviewView));
        _views[(int)AppTab.Execute] = executeView ?? throw new ArgumentNullException(nameof(executeView));
        _views[(int)AppTab.Undo] = undoView ?? throw new ArgumentNullException(nameof(undoView));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine("[red]The unified TUI requires a real terminal (not piped/redirected).[/]");
            AnsiConsole.MarkupLine("[dim]Use individual commands instead: mail-mopper fetch, mail-mopper classify, etc.[/]");
            return;
        }

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        await _db.Database.EnsureCreatedAsync(ct);

        while (_running)
        {
            ct.ThrowIfCancellationRequested();
            BuildAndRender();
            var key = Console.ReadKey(intercept: true);
            await HandleKeyAsync(key, ct);
        }

        FinalRender();
    }

    private void BuildAndRender()
    {
        try
        {
            AnsiConsole.Clear();
            var layout = BuildLayout();
            AnsiConsole.Write(layout);
        }
        catch (IOException)
        {
        }
    }

    private static void FinalRender()
    {
        try
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[bold blue]MailMopper[/]");
            AnsiConsole.MarkupLine("[dim]Goodbye![/]");
        }
        catch (IOException)
        {
        }
    }

    private Layout BuildLayout()
    {
        var header = BuildHeader();
        var tabs = BuildTabBar();
        var content = BuildContent();
        var footer = BuildFooter();

        var headerLayout = new Layout("Header").Size(4);
        headerLayout.Update(header);

        var tabsLayout = new Layout("Tabs").Size(1);
        tabsLayout.Update(tabs);

        var footerLayout = new Layout("Footer").Size(2);
        footerLayout.Update(footer);

        var contentLayout = new Layout("Content");
        contentLayout.Update(content);

        return new Layout("Root")
            .SplitRows(headerLayout, tabsLayout, contentLayout, footerLayout);
    }

    private IRenderable BuildHeader()
    {
        var authenticated = _session.IsAuthenticated;
        var emailLabel = authenticated ? "[bold green]●[/] Authenticated" : "[bold red]○[/] Not authenticated";
        var userInfo = authenticated ? GetUserEmail() ?? "Gmail" : "Not connected";

        if (!string.IsNullOrEmpty(_authStatus))
            emailLabel = $"[yellow]⟳ {Markup.Escape(_authStatus)}[/]";

        var grid = new Grid()
            .AddColumn(new GridColumn().Padding(0, 1))
            .AddColumn(new GridColumn().Padding(0, 1));

        grid.AddRow(
            new Panel(new Markup($"[bold blue]MailMopper[/]")).Border(BoxBorder.None).Padding(new Padding(0, 1)),
            new Markup($"[dim]{Markup.Escape(userInfo)}  {emailLabel}[/]")
                .RightJustified());

        grid.AddRow(
            new Markup(GetStatsLine()).Centered());

        return new Panel(grid)
            .Border(BoxBorder.Rounded)
            .Padding(new Padding(0, 1));
    }

    private string GetUserEmail()
    {
        try
        {
            var profile = _session.Service?.Users.GetProfile("me").ExecuteAsync(CancellationToken.None).Result;
            return profile?.EmailAddress ?? "Gmail";
        }
        catch
        {
            return "Gmail";
        }
    }

    private string GetStatsLine()
    {
        try
        {
            var totalEmails = _db.Emails.Count();
            var classified = _db.Classifications.Select(c => c.MessageId).Distinct().Count();
            var approved = _db.Classifications.Count(c => c.ReviewDecision == Models.ReviewDecision.ApproveTrash);
            var trashed = _db.Actions.Count(a => a.Action == "trash");
            var totalSize = _db.Emails.Sum(e => e.SizeEstimate);

            return $"{totalEmails:N0} emails  │  {classified:N0} classified  │  {approved:N0} approved  │  {trashed:N0} trashed  │  {FormatSize(totalSize)}";
        }
        catch
        {
            return "Loading stats...";
        }
    }

    private IRenderable BuildTabBar()
    {
        var cols = new List<IRenderable>();
        foreach (var (tab, label, hotkey) in _tabs)
        {
            var isActive = tab == _currentTab;
            var prefix = isActive ? "[bold black on blue]" : "[dim]";
            var suffix = isActive ? "[/]" : "[/]";
            cols.Add(new Markup($"{prefix} [[{hotkey}]] {label} {suffix}"));
        }
        return new Columns(cols).Padding(1, 1).Expand();
    }

    private IRenderable BuildContent()
    {
        if (!string.IsNullOrEmpty(_authStatus))
        {
            var rows = new List<IRenderable>
            {
                new Markup($"[yellow]⟳ {Markup.Escape(_authStatus)}[/]"),
            };

            if (!string.IsNullOrEmpty(_authUrl))
            {
                rows.Add(new Markup(""));
                rows.Add(new Markup("  [bold cyan]Click this URL to authenticate:[/]"));
                rows.Add(new Markup($"  [underline blue]{_authUrl}[/]"));
                rows.Add(new Markup(""));
                rows.Add(new Markup("  [dim]If the link doesn't work, copy and paste it into your browser.[/]"));
                rows.Add(new Markup("  [dim]The URL is also printed to the console for copying.[/]"));
            }

            return Align.Center(new Rows(rows), VerticalAlignment.Middle);
        }

        if (_showHelp)
            return HelpView.GetContent();

        var view = _views[(int)_currentTab];
        var contentHeight = Console.WindowHeight - 7;
        return view.GetContent(contentHeight);
    }

    private IRenderable BuildFooter()
    {
        var hints = new List<string> { "[bold]1-6[/]:Tabs", "[bold]Tab[/]/[bold]Shift+Tab[/]:Cycle", "[bold]Q[/]:Quit", "[bold]F1[/]:Help" };

        var view = _views[(int)_currentTab];
        var viewHints = view.GetFooterHints();
        if (!string.IsNullOrEmpty(viewHints))
            hints.Add(viewHints);

        var grid = new Grid()
            .AddColumn(new GridColumn().Padding(0, 1));

        grid.AddRow(new Markup(string.Join("  │  ", hints.Select(h => $"[dim]{h}[/]"))).Centered());
        grid.AddRow(new Markup(viewHints).Centered());

        return new Panel(grid)
            .Border(BoxBorder.Rounded)
            .Padding(new Padding(0, 1));
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key, CancellationToken ct)
    {
        if (_showHelp)
        {
            if (key.Key is ConsoleKey.F1 or ConsoleKey.Oem2 || key.KeyChar is '?' or '/')
            {
                _showHelp = false;
                return;
            }
            _showHelp = false;
            return;
        }

        if (key.Key is ConsoleKey.F1 || key.KeyChar is '?' or '/')
        {
            _showHelp = true;
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.D1 or ConsoleKey.NumPad1:
                _currentTab = AppTab.Home;
                return;
            case ConsoleKey.D2 or ConsoleKey.NumPad2:
                _currentTab = AppTab.Fetch;
                return;
            case ConsoleKey.D3 or ConsoleKey.NumPad3:
                _currentTab = AppTab.Classify;
                return;
            case ConsoleKey.D4 or ConsoleKey.NumPad4:
                _currentTab = AppTab.Review;
                return;
            case ConsoleKey.D5 or ConsoleKey.NumPad5:
                _currentTab = AppTab.Execute;
                return;
            case ConsoleKey.D6 or ConsoleKey.NumPad6:
                _currentTab = AppTab.Undo;
                return;
            case ConsoleKey.Tab:
                _currentTab = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                    ? PreviousTab()
                    : NextTab();
                return;
        }

        if (char.ToUpperInvariant(key.KeyChar) == 'Q')
        {
            _running = false;
            return;
        }

        var view = _views[(int)_currentTab];
        var command = await view.HandleInputAsync(key, ct);

        switch (command)
        {
            case ViewCommand.Quit:
                _running = false;
                break;
            case ViewCommand.GoToHome:
                _currentTab = AppTab.Home;
                break;
            case ViewCommand.GoToFetch:
                _currentTab = AppTab.Fetch;
                break;
            case ViewCommand.GoToClassify:
                _currentTab = AppTab.Classify;
                break;
            case ViewCommand.GoToReview:
                _currentTab = AppTab.Review;
                break;
            case ViewCommand.GoToExecute:
                _currentTab = AppTab.Execute;
                break;
            case ViewCommand.GoToUndo:
                _currentTab = AppTab.Undo;
                break;
            case ViewCommand.OpenHelp:
                _showHelp = true;
                break;
            case ViewCommand.RequestAuth:
                await HandleAuthAsync(ct);
                break;
            case ViewCommand.RequestClassify:
                _currentTab = AppTab.Classify;
                break;
            case ViewCommand.MarkDirty:
                break;
        }
    }

    private string _authStatus = "";
    private string _authUrl = "";
    private Task? _authTask;

    private async Task HandleAuthAsync(CancellationToken ct)
    {
        if (_authTask != null && !_authTask.IsCompleted)
            return;

        _authUrl = "";
        _authStatus = "Generating authentication URL...";
        var authService = new GmailAuthService(_settings, _session);

        _authTask = Task.Run(async () =>
        {
            try
            {
                await authService.AuthenticateAsync(ct, onAuthUrl: url =>
                {
                    _authUrl = url;
                    _authStatus = "Authentication in progress — open the URL below in your browser:";
                });
                _authStatus = "[green]✓ Authenticated![/]";
                _authUrl = "";
            }
            catch (Exception ex)
            {
                _authStatus = $"[red]Auth failed: {ex.Message}[/]";
                _authUrl = "";
            }
            await Task.Delay(2000, ct);
            _authStatus = "";
        }, ct);
    }

    private AppTab NextTab()
    {
        var current = (int)_currentTab;
        var count = _tabs.Length;
        return (AppTab)((current + 1) % count);
    }

    private AppTab PreviousTab()
    {
        var current = (int)_currentTab;
        var count = _tabs.Length;
        return (AppTab)((current - 1 + count) % count);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}

public static class Centering
{
    public static IRenderable Center(IRenderable content, int availableHeight)
    {
        return Align.Center(content, VerticalAlignment.Middle);
    }
}

using Spectre.Console.Rendering;

namespace MailMopper.Tui.Views;

public enum ViewCommand
{
    None,
    Quit,
    GoToHome,
    GoToFetch,
    GoToClassify,
    GoToReview,
    GoToExecute,
    GoToUndo,
    OpenHelp,
    RequestAuth,
    RequestClassify,
    MarkDirty,
}

public interface IAppView
{
    IRenderable GetContent(int availableHeight);
    string GetFooterHints();
    Task<ViewCommand> HandleInputAsync(ConsoleKeyInfo key, CancellationToken ct);
}

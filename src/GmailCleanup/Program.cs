using System.Reflection;
using GmailCleanup.Commands;
using Spectre.Console.Cli;

var version = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "0.0.0";

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("gmail-cleanup");
    config.SetApplicationVersion(version);

    config.AddCommand<AuthCommand>("auth")
        .WithDescription("Set up Gmail OAuth2 authentication");

    config.AddCommand<FetchCommand>("fetch")
        .WithDescription("Fetch/sync email metadata from Gmail");

    config.AddCommand<ClassifyCommand>("classify")
        .WithDescription("Run classification pipeline (rules + ML)");

    config.AddCommand<TrainCommand>("train")
        .WithDescription("Train ML classifier on rule-classified emails");

    config.AddCommand<ReviewCommand>("review")
        .WithDescription("Open TUI to review classifications");

    config.AddCommand<ExecuteCommand>("execute")
        .WithDescription("Trash approved emails");

    config.AddCommand<StatsCommand>("stats")
        .WithDescription("Show email statistics and category breakdown");

    config.AddCommand<UndoCommand>("undo")
        .WithDescription("Undo a previous trash session");

    config.AddCommand<RepairDatesCommand>("repair-dates")
        .WithDescription("Fix emails with incorrect dates (re-fetch from Gmail)");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Full pipeline: fetch → classify → review → execute");
});

return await app.RunAsync(args);

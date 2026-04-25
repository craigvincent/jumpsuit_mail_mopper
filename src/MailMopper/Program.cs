using System.Reflection;
using MailMopper.Commands;
using MailMopper.Data;
using MailMopper.Infrastructure;
using MailMopper.Services;
using MailMopper.Tui;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

var version = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "0.0.0";

// Build service collection
var services = new ServiceCollection();

var appSettings = CommandHelper.LoadSettings();
services.AddSingleton(appSettings);
services.AddSingleton(_ => new AppDbContext());
services.AddTransient<GmailAuthService>();
services.AddTransient<RuleClassifier>();
services.AddTransient<DatabaseService>();
services.AddTransient<ModelTrainerService>();
services.AddTransient<ReviewApp>();

var app = new CommandApp(new TypeRegistrar(services));

app.Configure(config =>
{
    config.SetApplicationName("mail-mopper");
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

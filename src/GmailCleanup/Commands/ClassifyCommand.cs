using System.ComponentModel;
using System.Globalization;
using GmailCleanup.Services;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GmailCleanup.Commands;

public class ClassifySettings : CommandSettings
{
    [CommandOption("--skip-ml")]
    [Description("Skip ML classification, use rules only")]
    public bool SkipMl { get; set; }
}

public class ClassifyCommand : AsyncCommand<ClassifySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ClassifySettings settings)
    {
        try
        {
            Console.WriteLine("=== Email Classification ===");

            var appSettings = CommandHelper.LoadSettings();
            using var dbContext = CommandHelper.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            var ruleClassifier = new RuleClassifier(appSettings);

            // Try to load ML model if not skipping
            using MlClassifier? mlClassifier = !settings.SkipMl
                ? TryLoadMlClassifier(appSettings)
                : null;

            var pipeline = new ClassificationPipeline(ruleClassifier, mlClassifier, dbContext, appSettings);

            var totalEmails = await dbContext.Emails.CountAsync();
            var classifiedCount = await dbContext.Classifications.CountAsync();
            var unclassifiedCount = totalEmails - classifiedCount;

            Console.WriteLine($"  {unclassifiedCount} emails to classify");
            Console.WriteLine();

            // Direct synchronous callback — no Progress<T>, no Spectre widgets
            var summary = await pipeline.RunAsync(
                settings.SkipMl,
                onStatus: msg => Console.WriteLine($"  {msg}"),
                CancellationToken.None);

            Console.WriteLine();

            // Get category breakdown
            var categoryBreakdown = await dbContext.Classifications
                .GroupBy(c => c.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(CancellationToken.None);

            // Display results with Spectre table (safe — pipeline is done)
            AnsiConsole.MarkupLine("[green]✓ Classification complete![/]");
            AnsiConsole.MarkupLine($"  [bold]Rules:[/] {summary.RuleClassified}  [bold]ML:[/] {summary.MlClassified}  [bold]Unclassified:[/] {summary.Unclassified}");

            var table = new Table();
            table.AddColumn("[bold]Category[/]");
            table.AddColumn("[bold]Count[/]", col => col.RightAligned());
            table.AddColumn("[bold]Percentage[/]", col => col.RightAligned());

            var total = categoryBreakdown.Sum(x => x.Count);
            foreach (var item in categoryBreakdown)
            {
                var percentage = total > 0 ? (item.Count * 100.0 / total) : 0;
                table.AddRow(item.Category.ToString(), item.Count.ToString(CultureInfo.InvariantCulture), $"{percentage:F1}%");
            }

            AnsiConsole.Write(table);

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during classification: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static MlClassifier? TryLoadMlClassifier(Config.AppSettings appSettings)
    {
        var modelPath = appSettings.Ml?.ModelPath ?? ModelTrainerService.GetDefaultModelPath();
        if (File.Exists(modelPath))
        {
            Console.WriteLine("  ML model loaded");
            return new MlClassifier(appSettings, modelPath);
        }
        Console.WriteLine("  No ML model found. Run 'gmail-cleanup train' to train, or use --skip-ml.");
        Console.WriteLine("  Proceeding with rules only.");
        return null;
    }
}

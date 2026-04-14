---
description: "Scaffold a new Spectre.Console.Cli command with settings class, following existing project patterns"
agent: "agent"
argument-hint: "command name and description, e.g. 'export - Export classification results to CSV'"
---
Create a new CLI command for the MailMopper project. Follow these patterns exactly:

## File structure

Create `src/MailMopper/Commands/{Name}Command.cs` with:

1. **Settings class** (if command has options): `{Name}Settings : CommandSettings` with `[CommandOption]` and `[Description]` attributes
2. **Command class**: `{Name}Command : AsyncCommand<{Name}Settings>` (or `AsyncCommand` if no options)

## Template

```csharp
using Spectre.Console;
using Spectre.Console.Cli;
using MailMopper.Data;
using MailMopper.Services;
using System.ComponentModel;

namespace MailMopper.Commands;

public class {Name}Settings : CommandSettings
{
    // [CommandOption("--flag")]
    // [Description("Description of the flag")]
    // public bool Flag { get; set; }
}

public class {Name}Command : AsyncCommand<{Name}Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, {Name}Settings settings)
    {
        try
        {
            var appSettings = CommandHelper.LoadSettings();
            using var dbContext = CommandHelper.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            // TODO: implement command logic

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}
```

## Registration

After creating the command file, register it in `src/MailMopper/Program.cs` inside the `config.AddCommand<>()` block:

```csharp
config.AddCommand<{Name}Command>("{kebab-name}")
    .WithDescription("{description}");
```

## Conventions

- Use `CommandHelper.LoadSettings()` and `CommandHelper.CreateDbContext()` for setup
- Wrap body in try-catch, return `0` for success, `1` for error
- Use `AnsiConsole.MarkupLine()` for styled output
- Accept `CancellationToken.None` when calling async services

# Project Guidelines

## Overview

C# / .NET 10 CLI tool for Gmail inbox cleanup. Hybrid classification pipeline (rule-based + ML.NET) with interactive TUI review. See [README.md](README.md) for full feature list and usage workflow.

## Build and Test

```bash
dotnet build
dotnet test
dotnet run --project src/GmailCleanup -- <command>
```

Commands: `auth`, `fetch`, `classify`, `train`, `review`, `execute`, `stats`, `undo`, `repair-dates`, `run`

## Architecture

- **CLI framework**: Spectre.Console.Cli — each command extends `AsyncCommand<TSettings>`
- **Database**: EF Core 10 + SQLite at `%LOCALAPPDATA%/GmailCleanup/gmail_cleanup.db`
- **No DI container**: Commands instantiate services directly via `CommandHelper` static helpers
- **Gmail abstraction**: `IGmailApi` interface wraps Google.Apis.Gmail.v1 for testability
- **Classification pipeline**: Rules (priority-ordered) → ML.NET → "Unclassified" fallback
- **Config loading**: `appsettings.json` → env vars (`GMAIL_CLEANUP_` prefix), bound to `AppSettings` POCOs

### Key directories

| Path | Purpose |
|------|---------|
| `src/GmailCleanup/Commands/` | CLI commands with settings classes |
| `src/GmailCleanup/Services/` | Business logic (classification, Gmail API, DB operations) |
| `src/GmailCleanup/Models/` | EF Core entities and enums |
| `src/GmailCleanup/Tui/` | Terminal UI for interactive review |
| `rules/` | JSON rule definitions for classification |
| `tests/GmailCleanup.Tests/` | xUnit tests with NSubstitute mocks |

## Conventions

- **Nullable reference types** enabled — use `?? throw new ArgumentNullException()` for required params
- **Async everywhere** for I/O — suffix methods with `Async`, always accept `CancellationToken`
- **Private fields**: `_camelCase`; methods: `PascalCase`; params: `camelCase`
- **One class per file**, namespace mirrors folder structure (`GmailCleanup.Services`, etc.)
- **Batch processing**: 500 emails for classification, 1000 for trash operations
- **Error handling**: try-catch at command level, return `1` for errors; constructor validation with null-coalescing throw

## Testing

- **Framework**: xUnit 2.9.3 + NSubstitute for mocking
- **Database**: In-memory SQLite (`DataSource=:memory:`) for test isolation
- **Test naming**: `MethodOrScenario_Condition_ExpectedResult` style
- **Factory helpers**: `MakeEmail()` with optional params for test data
- **Integration tests**: Use real `default-rules.json`, mock only `IGmailApi`

## Rules Configuration

Rules in `rules/default-rules.json` support types: `header`, `gmail-category`, `sender-domain`, `sender-pattern`, `subject-pattern`. Lower `priority` value = higher precedence.

## Safety

- `DryRunDefault: true` in appsettings — always default to dry-run
- Trash only (recoverable 30 days) — never permanently delete
- All data stored locally — no external API calls during classification

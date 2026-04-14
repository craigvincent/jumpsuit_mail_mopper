# 🔨 Development

For contributors and developers. If you're just using the tool, everything in the [README](README.md) is all you need.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://docs.docker.com/get-docker/) (for container builds)

## Build & Test

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes   # Check code style
```

## Run locally (without Docker)

```bash
dotnet run --project src/MailMopper -- auth
dotnet run --project src/MailMopper -- fetch
# etc.
```

When running locally, the default browser-based OAuth flow is used (no port mapping needed). Data is stored at `%LOCALAPPDATA%/MailMopper/` (Windows) or `~/.local/share/MailMopper/` (Linux/macOS).

## Docker Build

```bash
# Build the image locally
docker build -t mail_mopper .

# Run the locally-built image
docker run -it --rm mail_mopper --help
```

The image uses a multi-stage build with an [Ubuntu Chiseled](https://github.com/dotnet/dotnet-docker/blob/main/documentation/ubuntu-chiseled.md) (distroless) runtime — no shell, no package manager, ~50% smaller than standard images.

## Configuration

- `appsettings.json` - Batch sizes, confidence thresholds
- `rules/default-rules.json` - Customisable classification rules

## Static Analysis & Coverage

### .NET Analyzers (local)

Static analysis runs automatically on every build via settings in `Directory.Build.props`:

- `EnableNETAnalyzers` with `AnalysisLevel: latest-recommended`
- `TreatWarningsAsErrors` — all analyzer warnings are build errors
- `EnforceCodeStyleInBuild` — code style rules enforced at build time

No extra tooling needed — just `dotnet build`.

### Code Coverage (local)

```bash
dotnet test \
  --collect:"XPlat Code Coverage" \
  --settings tests/MailMopper.Tests/coverlet.runsettings \
  --results-directory ./coverage
```

Coverage reports (Cobertura + OpenCover XML) are written to `./coverage/`.

### SonarCloud (CI)

SonarCloud runs via CI-based scanner on every push and PR, including coverage upload. Results are posted to the [SonarCloud dashboard](https://sonarcloud.io).

**Setup:** Disable Automatic Analysis in SonarCloud (Administration → Analysis Method), then configure these in GitHub repo settings:
- **Secret:** `SONAR_TOKEN` — from SonarCloud → My Account → Security
- **Variable:** `SONAR_PROJECT_KEY` — your SonarCloud project key
- **Variable:** `SONAR_ORG` — your SonarCloud organisation key

## CI/CD

- **CI** (`ci.yml`): Runs on all PRs — formatting, build, tests, coverage reporting and SonarCloud static analysis
- **Deploy** (`deploy.yml`): Runs on merge to `main` — builds and pushes the Docker image to GitHub Container Registry (`ghcr.io`), then creates a GitHub Release

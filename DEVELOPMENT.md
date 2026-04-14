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

## CI/CD

- **CI** (`ci.yml`): Runs on all PRs — linting, formatting, build, and tests
- **Deploy** (`deploy.yml`): Runs on merge to `main` — builds and pushes the Docker image to GitHub Container Registry (`ghcr.io`), then creates a GitHub Release

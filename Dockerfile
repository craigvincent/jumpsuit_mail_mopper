FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.0.0-local
WORKDIR /src

COPY GmailCleanup.slnx Directory.Build.props ./
COPY src/GmailCleanup/GmailCleanup.csproj src/GmailCleanup/
COPY tests/GmailCleanup.Tests/GmailCleanup.Tests.csproj tests/GmailCleanup.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/GmailCleanup/GmailCleanup.csproj \
    --configuration Release \
    --no-restore \
    -p:Version=${VERSION} \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Auth callback port for OAuth2 loopback flow (set via GMAIL_CLEANUP_Gmail__AuthCallbackPort)
EXPOSE 8484
ENV GMAIL_CLEANUP_Gmail__AuthCallbackPort=8484

USER $APP_UID
ENTRYPOINT ["dotnet", "GmailCleanup.dll"]

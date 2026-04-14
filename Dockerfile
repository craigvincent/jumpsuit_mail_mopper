FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.0.0-local
WORKDIR /src

COPY MailMopper.slnx Directory.Build.props ./
COPY src/MailMopper/MailMopper.csproj src/MailMopper/
COPY tests/MailMopper.Tests/MailMopper.Tests.csproj tests/MailMopper.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/MailMopper/MailMopper.csproj \
    --configuration Release \
    --no-restore \
    -p:Version=${VERSION} \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Create data directory writable by the non-root user (UID 1654)
COPY --chown=$APP_UID:$APP_UID --from=build /src/Directory.Build.props /home/app/.local/share/MailMopper/.keep
# Auth callback port for OAuth2 loopback flow (set via MAIL_MOPPER_Gmail__AuthCallbackPort)
EXPOSE 8484
ENV MAIL_MOPPER_Gmail__AuthCallbackPort=8484
# Ensure LocalApplicationData resolves correctly for the non-root user
ENV HOME=/home/app
ENV XDG_DATA_HOME=/home/app/.local/share

USER $APP_UID
ENTRYPOINT ["dotnet", "MailMopper.dll"]

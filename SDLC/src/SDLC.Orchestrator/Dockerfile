FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SDLC.Orchestrator.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Note: SDLC.Orchestrator is a library project with no HTTP server.
# This Dockerfile is provided for completeness; no HEALTHCHECK applies.
FROM base AS final
WORKDIR /app
RUN groupadd -r app && useradd -r -g app -u 10001 app
USER app
COPY --from=build --chown=10001:10001 /app/publish .
ENTRYPOINT ["dotnet", "SDLC.Orchestrator.dll"]

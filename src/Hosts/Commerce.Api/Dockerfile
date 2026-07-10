# syntax=docker/dockerfile:1.7

ARG DOTNET_SDK_VERSION=10.0.301
ARG DOTNET_RUNTIME_VERSION=10.0.9
ARG BUILD_CONFIGURATION=Release

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build

ARG BUILD_CONFIGURATION

WORKDIR /src

COPY . .

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore \
    "src/Hosts/Commerce.Api/Commerce.Api.csproj" \
    --locked-mode

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish \
    "src/Hosts/Commerce.Api/Commerce.Api.csproj" \
    --configuration "${BUILD_CONFIGURATION}" \
    --output /app/publish \
    --no-restore \
    --no-self-contained \
    /p:UseAppHost=false \
    /p:ContinuousIntegrationBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final

USER root

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER app

HEALTHCHECK \
    --interval=10s \
    --timeout=3s \
    --start-period=15s \
    --retries=3 \
    CMD curl \
        --fail \
        --silent \
        --show-error \
        --max-time 2 \
        http://127.0.0.1:8080/health/live \
        || exit 1

ENTRYPOINT ["dotnet", "Commerce.Api.dll"]
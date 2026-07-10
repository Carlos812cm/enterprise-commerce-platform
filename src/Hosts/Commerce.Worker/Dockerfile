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
    "src/Hosts/Commerce.Worker/Commerce.Worker.csproj" \
    --locked-mode

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish \
    "src/Hosts/Commerce.Worker/Commerce.Worker.csproj" \
    --configuration "${BUILD_CONFIGURATION}" \
    --output /app/publish \
    --no-restore \
    --no-self-contained \
    /p:UseAppHost=false \
    /p:ContinuousIntegrationBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final

WORKDIR /app

COPY --from=build --chown=app:app /app/publish .

USER app

ENTRYPOINT ["dotnet", "Commerce.Worker.dll"]
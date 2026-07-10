# Application Containers

The platform packages its executable .NET hosts as Linux container images.

## Images

| Image | Host | Internal Port |
|---|---|---:|
| `enterprise-commerce-api` | `Commerce.Api` | `8080` |
| `enterprise-commerce-worker` | `Commerce.Worker` | None |

## Build Strategy

Both images use multi-stage Docker builds.

The build stage uses the .NET SDK image. The final stage contains only the published application and the ASP.NET Core runtime.

NuGet restore runs in locked mode and uses a BuildKit cache mount.

## Runtime Security

Both application images:

- Run as the built-in non-root `app` user.
- Drop all Linux capabilities in Docker Compose.
- Enable `no-new-privileges`.
- Handle graceful termination through the .NET Generic Host.

## API Healthcheck

The API image checks:

```text
http://127.0.0.1:8080/health/live

Docker marks the API container as unhealthy when the endpoint repeatedly fails.

Worker Runtime Image

The Worker currently uses the ASP.NET Core runtime image because Commerce.ServiceDefaults references the Microsoft.AspNetCore.App shared framework.

A future refactor may split host-neutral defaults from ASP.NET-specific endpoint mappings, allowing the Worker to use the smaller .NET runtime image.

Build Commands
docker compose build --pull commerce-api commerce-worker
docker compose up --detach
docker compose ps
Application Endpoints
API:        http://localhost:5000
Liveness:   http://localhost:5000/health/live
Readiness:  http://localhost:5000/health/ready
Current Image Trade-off

The API uses a standard Linux runtime image and installs curl for its container healthcheck.

Chiseled images are intentionally deferred until health probes and debugging workflows are adapted for images without a shell or package manager.
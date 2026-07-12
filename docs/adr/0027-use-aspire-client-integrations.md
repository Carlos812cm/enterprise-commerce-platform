# ADR-0027: Use Aspire Client Integrations

## Status

Accepted

## Context

The API and Worker require shared clients for PostgreSQL, Redis and RabbitMQ.

Creating separate clients for application use, health checks and telemetry would duplicate connections, increase resource usage and introduce inconsistent configuration.

## Decision

Use the official Aspire client integrations:

- `Aspire.Npgsql`
- `Aspire.StackExchange.Redis`
- `Aspire.RabbitMQ.Client`

The integrations register the underlying clients in dependency injection and enable health checks, logging and telemetry hooks.

Both executable hosts register the same three infrastructure clients through `Commerce.ServiceDefaults`.

Readiness executes all registered health checks, while liveness executes only the lightweight process self-check.

## Consequences

### Benefits

- One shared client registration per dependency.
- Built-in health checks.
- Built-in telemetry integration.
- Consistent connection configuration.
- Readiness reflects required infrastructure dependencies.
- Clients remain available through standard dependency-injection abstractions.

### Costs

- Both hosts currently depend on all three infrastructure services.
- Aspire integration packages become runtime dependencies.
- RabbitMQ connection creation becomes part of host infrastructure initialization.
- Local development requires all dependencies to be available for readiness.
- The Worker now exposes an internal HTTP health surface.

## Alternatives Considered

- **Handwritten health checks:** rejected because they would duplicate client and connection logic.
- **Raw client registration in each host:** rejected because it would duplicate configuration and observability setup.
- **Service-specific connection wrappers:** deferred until domain workflows require stronger application-facing abstractions.

## Related

- `docs/operations/health-checks.md`
- `docs/operations/infrastructure-clients.md`
- `docs/operations/observability.md`

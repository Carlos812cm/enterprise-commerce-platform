# ADR-0027: Use Aspire Client Integrations

## Status

Accepted

## Context

The API and Worker require shared clients for PostgreSQL, Redis and RabbitMQ.

Creating separate clients for application use, health checks and telemetry would duplicate connections and introduce inconsistent configuration.

## Decision

Use official Aspire client integrations:

- `Aspire.Npgsql`
- `Aspire.StackExchange.Redis`
- `Aspire.RabbitMQ.Client`

The integrations register the underlying clients in dependency injection and enable health checks, logging and telemetry.

Both executable hosts register the same three infrastructure clients through `Commerce.ServiceDefaults`.

## Consequences

Benefits:

- One shared client registration per dependency.
- Built-in health checks.
- Built-in telemetry integration.
- Consistent connection configuration.
- Readiness reflects actual dependencies.
- Clients remain available through standard DI abstractions.

Costs:

- Both hosts currently depend on all three infrastructure services.
- Aspire integration packages become runtime dependencies.
- RabbitMQ connection creation is part of host infrastructure initialization.
- Local development requires all dependencies to be available for readiness.

## Alternatives Considered

- Handwritten health checks: rejected because they would create duplicate client logic.
- Raw client registration in each host: rejected because it duplicates configuration.
- Service-specific connection wrappers: deferred until domain workflows require stronger abstractions.

## Related

- `docs/operations/health-checks.md`
- `docs/operations/infrastructure-clients.md`
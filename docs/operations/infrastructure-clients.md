# Infrastructure Clients

The executable hosts register shared clients for PostgreSQL, Redis and RabbitMQ through `Commerce.ServiceDefaults`.

## Registered Clients

| Connection name | Dependency-injection type | Integration package |
|---|---|---|
| `Postgres` | `NpgsqlDataSource` | `Aspire.Npgsql` |
| `Redis` | `IConnectionMultiplexer` | `Aspire.StackExchange.Redis` |
| `RabbitMq` | `RabbitMQ.Client.IConnection` | `Aspire.RabbitMQ.Client` |

Both `Commerce.Api` and `Commerce.Worker` call:

```csharp
builder.AddInfrastructureClients(serviceName);
```

This centralizes connection configuration, health checks, logging and telemetry registration.

## Health Semantics

`/health/live` validates only the running process.

`/health/ready` validates:

- The process self-check.
- PostgreSQL connectivity.
- Redis connectivity.
- RabbitMQ connectivity.

A dependency failure must not automatically terminate the process. The host remains alive while readiness reports that it should not receive traffic or work.

## Configuration

Connection strings are read from these configuration keys:

```text
ConnectionStrings:Postgres
ConnectionStrings:Redis
ConnectionStrings:RabbitMq
```

Their environment-variable equivalents are:

```text
ConnectionStrings__Postgres
ConnectionStrings__Redis
ConnectionStrings__RabbitMq
```

The base Docker Compose file supplies container-network connection strings, while `appsettings.Development.json` supplies localhost values for direct `dotnet run` development.

## Redis Resilience

Redis is configured with:

- `AbortOnConnectFail = false`
- Three initial connection retries.
- A five-second connection timeout.
- A service-specific client name.

This allows the shared multiplexer to continue reconnecting after a temporary outage instead of forcing the entire process to terminate.

## RabbitMQ Recovery

RabbitMQ is configured with:

- Automatic connection recovery.
- Automatic topology recovery.
- A five-second network-recovery interval.
- A five-second connection timeout.
- A thirty-second heartbeat.
- A service-specific client name.

These settings improve connection recovery, but they do not provide business-level message-delivery guarantees.

## Reliability Boundary

Reliable delivery will be implemented separately through:

- Transactional Outbox.
- Inbox deduplication.
- Idempotent consumers.
- Retry and dead-letter policies.
- Saga state and compensating actions.

Connection recovery keeps the transport available. It does not replace transactional messaging patterns.

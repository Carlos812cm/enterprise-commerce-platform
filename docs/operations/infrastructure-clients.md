# Infrastructure Clients

The executable hosts register shared clients for PostgreSQL, Redis and RabbitMQ.

## Registered Clients

| Connection name | DI type |
|---|---|
| `Postgres` | `NpgsqlDataSource` |
| `Redis` | `IConnectionMultiplexer` |
| `RabbitMq` | `RabbitMQ.Client.IConnection` |

## Health Semantics

`/health/live` checks only the running process.

`/health/ready` checks:

- Process self-check
- PostgreSQL
- Redis
- RabbitMQ

A dependency failure must not automatically terminate the process.

## Configuration

Connection strings are read from:

```text
ConnectionStrings:Postgres
ConnectionStrings:Redis
ConnectionStrings:RabbitMq

Environment variable equivalents:

ConnectionStrings__Postgres
ConnectionStrings__Redis
ConnectionStrings__RabbitMq

Resilience

Redis is configured to continue reconnecting after connection loss.

RabbitMQ uses automatic connection and topology recovery.

Business-level delivery guarantees are not provided by these connection settings. Reliable delivery will be implemented through Outbox, Inbox, idempotent consumers and messaging workflows.
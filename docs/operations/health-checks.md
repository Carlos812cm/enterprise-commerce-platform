# Health Checks

The platform exposes health endpoints for operational monitoring.

## Endpoints

| Endpoint | Purpose |
|---|---|
| `/health/live` | Indicates whether the process is alive. |
| `/health/ready` | Indicates whether the process is ready to receive traffic. |

## Current Checks

| Check | Tags | Description |
|---|---|---|
| `self` | `live`, `ready` | Confirms the host process is running. |

## Future Checks

Future infrastructure checks will include:

- PostgreSQL
- Redis
- RabbitMQ
- Keycloak connectivity
- Outbox backlog
- Worker readiness

## Rule

Liveness checks must stay lightweight.

Readiness checks may validate dependencies, but they must avoid expensive queries or operations that could overload infrastructure.

```text
self
PostgreSQL
Redis
RabbitMQ
# Health Checks

The platform exposes separate health endpoints for process liveness and operational readiness.

## Endpoints

| Endpoint | Purpose | Expected failure response |
|---|---|---:|
| `/health/live` | Confirms that the host process is running. | `503` only when the process-level self-check fails. |
| `/health/ready` | Confirms that the host and its required infrastructure dependencies can operate. | `503` when any registered readiness dependency is unhealthy. |

## Registered Checks

| Check | Liveness | Readiness | Description |
|---|---:|---:|---|
| `self` | Yes | Yes | Confirms that the host process is running. |
| PostgreSQL | No | Yes | Verifies connectivity through the registered Npgsql integration. |
| Redis | No | Yes | Verifies connectivity through the registered StackExchange.Redis integration. |
| RabbitMQ | No | Yes | Verifies connectivity through the registered RabbitMQ client integration. |

`/health/live` filters checks by the `live` tag and therefore executes only the lightweight process self-check.

`/health/ready` executes all registered checks. This includes the process self-check and the PostgreSQL, Redis and RabbitMQ dependency checks.

## Failure Semantics

A dependency outage must not automatically terminate or restart an otherwise healthy process.

For example, when Redis is unavailable:

- `/health/live` should continue returning `200 OK`.
- `/health/ready` should return `503 Service Unavailable`.
- The Docker healthcheck should continue reporting the container as alive because it calls `/health/live`.
- Readiness should recover automatically after Redis becomes available again.

## Operational Rules

- Liveness checks must remain lightweight and must not call external dependencies.
- Readiness checks may validate required dependencies, but they must avoid expensive queries or mutating operations.
- A health check is not a substitute for business-level validation, delivery guarantees or data consistency checks.

## Future Checks

Additional readiness signals may be introduced when their corresponding capabilities exist:

- Keycloak connectivity after authentication is integrated.
- Outbox backlog and publisher health.
- Message-consumer readiness.
- Search-index availability.

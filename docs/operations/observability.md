# Observability

The platform uses OpenTelemetry as its vendor-neutral telemetry standard.

## Signal Flow

```text
Commerce.Api / Commerce.Worker
             |
             | OTLP gRPC
             v
OpenTelemetry Collector
       |             |
       | metrics     | traces
       v             v
  Prometheus       Tempo
       \             /
        \           /
           Grafana
```

## Components

| Component | Purpose | Local port |
|---|---|---:|
| OpenTelemetry Collector | Telemetry gateway and routing layer | `4317`, `4318`, `13133` |
| Prometheus | Metrics storage and query | `9090` |
| Tempo | Trace storage and query | `3200` |
| Grafana | Metrics and trace exploration | `3000` |

## Start

```bash
docker compose \
  -f docker-compose.yml \
  -f deploy/compose/observability.yml \
  up --build --detach
```

## Stop

```bash
docker compose \
  -f docker-compose.yml \
  -f deploy/compose/observability.yml \
  down
```

Persistent observability data remains in named Docker volumes unless the environment is stopped with the `--volumes` option.

## Current Telemetry

### Commerce.Api

- ASP.NET Core request metrics.
- ASP.NET Core request traces.
- `HttpClient` metrics and traces.
- .NET runtime metrics.
- Structured console logs with trace and span identifiers.
- Telemetry hooks registered by the PostgreSQL, Redis and RabbitMQ Aspire integrations.

### Commerce.Worker

- `HttpClient` metrics and traces.
- .NET runtime metrics.
- Structured console logs with trace and span identifiers.
- Telemetry hooks registered by the PostgreSQL, Redis and RabbitMQ Aspire integrations.

The infrastructure clients are now registered and observable. Meaningful dependency spans and metrics will appear as business workflows begin executing database commands, cache operations and broker activity.

## Noise Control

Health endpoints are excluded from distributed traces.

They remain available for health monitoring and may still contribute HTTP metrics. This prevents liveness and readiness polling from flooding Tempo with low-value traces.

## Health and Observability

Health checks answer whether a host is alive or operationally ready. Telemetry explains why behavior changed and how requests or background operations moved through the system.

These mechanisms complement each other:

- Health checks provide a current operational verdict.
- Metrics reveal trends and saturation.
- Traces reveal causal request and dependency flows.
- Logs preserve structured diagnostic context.

## Current Limitations

- Logs are not yet stored in a centralized log backend.
- The Worker does not yet process message-broker traffic.
- No business transaction currently exercises all infrastructure clients end to end.
- Tempo uses local single-binary storage.
- No production retention, alerting or notification policy exists yet.

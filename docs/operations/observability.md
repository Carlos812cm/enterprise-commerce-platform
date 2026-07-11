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

Components
Component	Purpose	Local Port
OpenTelemetry Collector	Telemetry gateway	4317, 4318, 13133
Prometheus	Metrics storage and query	9090
Tempo	Trace storage and query	3200
Grafana	Visualization and exploration	3000
Start
docker compose \
  -f docker-compose.yml \
  -f deploy/compose/observability.yml \
  up --build --detach
Stop
docker compose \
  -f docker-compose.yml \
  -f deploy/compose/observability.yml \
  down

Persistent observability data remains in named Docker volumes.

Current Telemetry
API
ASP.NET Core request metrics
ASP.NET Core request traces
HttpClient metrics and traces
.NET runtime metrics
Structured console logs
Worker
HttpClient metrics and traces
.NET runtime metrics
Structured console logs
Noise Control

Health endpoints are excluded from distributed traces.

They remain available for health monitoring and may still contribute HTTP metrics.

Current Limitations
Logs are not yet stored centrally.
The Worker does not yet process message-broker traffic.
PostgreSQL, Redis and RabbitMQ clients are not instrumented yet.
Tempo uses local single-binary storage.
No production retention or alerting policy exists yet.
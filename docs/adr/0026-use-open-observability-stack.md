# ADR-0026: Use an Open Observability Stack

## Status

Accepted

## Context

The platform requires vendor-neutral telemetry for traces, metrics and structured operational diagnosis.

The application hosts already emit OpenTelemetry-compatible signals, but the repository does not yet provide local collection, storage or visualization.

## Decision

Use the following local observability stack:

- OpenTelemetry Collector as the telemetry gateway.
- Prometheus as the metrics backend.
- Grafana Tempo as the trace backend.
- Grafana as the visualization and exploration layer.
- Serilog console output for structured logs.

Application hosts export OTLP to the Collector.

The Collector exports:

- Metrics through a Prometheus-compatible endpoint.
- Traces to Tempo through OTLP gRPC.

## Consequences

Benefits:

- Vendor-neutral application instrumentation.
- Centralized telemetry routing.
- Reproducible local observability.
- Independent metrics and trace backends.
- Infrastructure suitable for future dashboards and alerting.

Costs:

- Additional containers and local resource usage.
- More configuration files to govern.
- The local Tempo backend is not a production-scale deployment.
- Logs are not yet stored in a centralized log backend.

## Alternatives Considered

- Export directly from applications to each backend: rejected because it couples applications to backend topology.
- Jaeger all-in-one: rejected because Tempo aligns better with the Grafana stack and future trace-to-metrics workflows.
- Commercial SaaS observability: deferred to preserve local reproducibility and avoid vendor dependency.
- Loki in the same phase: deferred until the platform produces meaningful business logs.

## Related

- `docs/operations/observability.md`
- `docs/operations/service-defaults.md`
- `deploy/compose/observability.yml`
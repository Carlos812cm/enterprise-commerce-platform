# Service Defaults

`Commerce.ServiceDefaults` centralizes host-level operational concerns shared by executable processes.

## Responsibilities

- Structured logging
- OpenTelemetry traces and metrics
- Default HttpClient resilience
- Service discovery
- Health checks
- Default health endpoints for HTTP hosts

## Current Hosts

- `Commerce.Api`
- `Commerce.Worker`

## Health Endpoints

`Commerce.Api` exposes:

- `/health/live`
- `/health/ready`

## OpenTelemetry

OpenTelemetry is configured for:

- ASP.NET Core instrumentation
- HttpClient instrumentation

OTLP export is enabled only when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured.

## Logging

Serilog is configured with:

- Application
- Environment
- MachineName
- ThreadId
- Log context

## Design Rule

Business logic must not be added to `Commerce.ServiceDefaults`.

This project exists only for cross-cutting host configuration.
# ADR-0030: Use a Decorable Command Dispatcher

## Status

Accepted

## Context

Application commands require runtime dispatch and cross-cutting behaviors for telemetry, logging, validation, authorization and transactions.

Commands and handlers must remain independent from a mediator vendor.

## Decision

Use a platform-owned command dispatcher based on:

- `ICommandDispatcher`
- Keyed dependency-injection registrations
- Generic command invokers
- Ordered command behaviors

Commands and handlers remain framework-neutral.

The Microsoft dependency-injection container is used only as the runtime composition mechanism.

## Behavior Order

Lower values execute farther outside the pipeline.

```text
Telemetry
-> Logging
-> Validation
-> Authorization
-> Transaction
-> Handler
```

# Observability

Every command emits:

- An internal Activity span.
- An execution counter.
- A duration histogram.
- Structured lifecycle logs.

Command payloads are not logged or added as telemetry tags.

# Transactions

The transaction order is reserved, but no transaction behavior is registered until a persistence implementation exists.

Handlers continue coordinating their current Unit of Work during this transitional phase.

# Consequences

Benefits:

- No mediator dependency in commands or handlers.
- Deterministic decorator ordering.
- Typed runtime dispatch.
- No reflection-based method invocation.
- Centralized logging and telemetry.
- Easy unit testing.

Costs:

- The platform owns dispatcher maintenance.
- Every command must be registered explicitly.
- Keyed DI is part of the runtime implementation.
- Transaction behavior remains deferred until Infrastructure exists.

# Related
- ADR-0029
- `docs/application/command-dispatcher.md`
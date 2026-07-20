# ADR-0029: Keep Application Messaging Framework-Neutral

## Status

Accepted

## Context

The platform requires command and query handlers with support for validation, transactions, logging, tracing and authorization behaviors.

A mediator framework can provide runtime dispatch, but coupling every command and handler directly to one framework makes the Application layer dependent on its licensing, registration and dispatch model.

The current Catalog slice contains one command and does not yet require a general-purpose runtime dispatcher.

## Decision

Define framework-neutral Application contracts:

- `Command<TResponse>`
- `ICommandHandler<TCommand, TResponse>`

Handlers return the platform `Result<T>` type.

No mediator package is referenced by the Application projects in this phase.

Dispatch and dependency-injection registration will be implemented at the composition boundary.

## Consequences

Benefits:

- Application handlers remain independent from a mediator vendor.
- Handler tests require no container or mediator runtime.
- A dispatcher can be replaced without rewriting use cases.
- Licensing and package changes remain isolated from business logic.
- Cross-cutting behaviors can be introduced deliberately.

Costs:

- The platform must provide registration and dispatch later.
- Pipeline behaviors are not available automatically.
- Additional conventions must be documented and tested.

## Alternatives Considered

### MediatR

Deferred because the current version introduces license-key configuration and the first slice does not justify the dependency.

### Direct Handler Injection Only

Acceptable for the first endpoint, but insufficient as a long-term convention without shared command contracts.

### Source-Generated Mediator

A viable future runtime implementation, provided it remains behind the platform Application contracts.

## Re-evaluation

Re-evaluate when:

- Multiple commands and queries require runtime dispatch.
- Validation, transaction and telemetry behaviors become repetitive.
- The API composition root needs automatic handler discovery.
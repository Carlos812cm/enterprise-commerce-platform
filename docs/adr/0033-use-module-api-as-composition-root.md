# ADR-0033: Use Module API as the Module Composition Root

## Status

Accepted

## Context

The executable API host references module API projects but not module Infrastructure projects.

Catalog HTTP endpoints require Application handlers and Infrastructure implementations to be registered together.

## Decision

Each `Module.Api` project is the composition root for its module.

A module API may reference:

- Its Application project
- Its Infrastructure project
- Its Contracts project

The executable host references only module API projects and shared host defaults.

## Consequences

Benefits:

- Module wiring remains encapsulated.
- The host does not know module persistence details.
- Endpoints, policies, Application and Infrastructure are registered together.
- Module removal remains localized.

Costs:

- Module API projects depend on Infrastructure.
- Architecture tests must distinguish composition dependencies from business dependencies.
- Module API projects must not contain business rules.

## Security

Authorization policies are declared by the module that owns the protected capability.

Authentication remains a host-level responsibility.

## Related

- ADR-0029
- ADR-0030
- ADR-0031
- ADR-0032
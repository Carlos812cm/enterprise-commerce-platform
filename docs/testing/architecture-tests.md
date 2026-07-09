# Architecture Tests

This project uses executable architecture tests to enforce modular boundaries.

## Current Enforcement Level

The current tests validate project-level dependencies by reading `.csproj` files.

They protect the modular monolith structure before business logic is implemented.

## Rules

- Domain projects may only reference `Commerce.Domain`.
- Contracts projects may only reference `Commerce.Contracts`.
- Application projects may only reference their own Domain/Contracts projects and `Commerce.Application`.
- Infrastructure projects may reference their own module projects and `Commerce.Infrastructure`.
- Module Api projects may only reference their own Application and Contracts projects.
- Module projects must not reference projects from another module.
- `Commerce.Api` may only reference module Api projects.
- `Commerce.Worker` may reference Application, Infrastructure and Contracts projects, but not Api or Domain projects directly.

## Why Project-Level Tests First

At this stage, the repository contains module boundaries but almost no domain types.

Project-level tests catch the most dangerous architectural violations early:

- Cross-module references
- Infrastructure leaking into Domain
- Api leaking into Application
- Hosts bypassing module boundaries

Type-level and bytecode-level architecture tests will be introduced later when domain models, handlers, endpoints and adapters exist.

## Future Enhancements

Possible next steps:

- ArchUnitNET for type-level dependency checks.
- Naming rules for handlers, endpoints and domain events.
- Rules preventing controllers/endpoints from referencing persistence directly.
- Rules for integration event versioning.
- Rules for module registration conventions.
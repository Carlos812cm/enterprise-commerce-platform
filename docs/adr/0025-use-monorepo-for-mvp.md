# ADR-0025: Use a Monorepo for the MVP

## Status

Accepted

## Context

The platform is an enterprise hybrid e-commerce system with multiple bounded contexts, including Catalog, Pricing, Inventory, Checkout, Ordering, Payments, and B2B Procurement.

At the MVP stage, the domain boundaries are still evolving. Splitting repositories too early would increase coordination overhead without providing real operational independence.

## Decision

Use a single monorepo for the MVP.

The repository will contain all hosts, modules, tests, documentation, deployment files, scripts, and architecture records.

## Consequences

Benefits:

- Easier refactoring across module boundaries.
- Unified CI/CD pipeline.
- Centralized documentation.
- Easier portfolio review.
- Lower coordination overhead.

Costs:

- Requires strict module boundaries.
- Requires architecture tests to prevent unwanted coupling.
- Requires disciplined ownership conventions.

## Alternatives Considered

- Polyrepo per service: rejected for MVP due to premature distribution.
- Single project solution: rejected because it hides domain boundaries.
- Full microservices from day one: rejected due to operational complexity and lack of current need.

## Related

- ADR-014: Start with a modular monolith instead of premature microservices.
- ADR-026: Adopt trunk-based development.

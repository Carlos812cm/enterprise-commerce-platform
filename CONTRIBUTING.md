# Contributing

This project follows an enterprise-style engineering workflow.

## Workflow

1. Create or select an issue.
2. Create a short-lived branch.
3. Implement a small change.
4. Run relevant checks locally.
5. Open a Pull Request.
6. Complete self-review.
7. Merge only after quality gates pass.

## Branch Naming

- feature/catalog-product-publishing
- fix/inventory-reservation-race-condition
- docs/adr-rabbitmq-broker
- test/checkout-idempotency
- chore/update-dependencies

## Commit Convention

Use Conventional Commits:

```text
<type>(<scope>): <description>

Examples:

feat(inventory): add atomic stock reservation
fix(checkout): prevent duplicate order submission
docs(adr): record monorepo decision
ci(github): add pull request validation workflow
Local Checks
dotnet restore
dotnet build --configuration Release
docker compose up -d
Architecture Rules
Domain projects must not depend on Infrastructure.
Modules must not write to another module's data store directly.
Business logic must not live in API endpoints.
Critical operations must be idempotent.
Integration events must be versioned contracts.

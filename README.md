# Enterprise Commerce Platform

Enterprise-grade hybrid e-commerce platform demo combining B2C flash sales and B2B procurement workflows.

## North Star

Apple-grade visual clarity  
+ Amazon-grade transactional intensity  
+ Grainger-grade B2B utility  
+ VTEX/SAP/commercetools-grade enterprise logic  
+ Baymard-grade UX discipline

## Current Status

Repository foundation phase.

No business logic has been implemented yet. This repository currently contains:

- .NET solution skeleton
- Modular architecture boundaries
- Docker Compose infrastructure baseline
- Documentation structure
- Initial engineering governance files

## Core Scenarios

1. B2C Flash Sale Checkout
2. B2B Private Catalog + Approval Workflow

## Tech Direction

- .NET 10 LTS
- ASP.NET Core
- PostgreSQL
- Redis
- RabbitMQ
- Keycloak
- OpenTelemetry
- Testcontainers
- k6
- Docker
- GitHub Actions

## Run Locally

```bash
dotnet restore
dotnet build
docker compose up -d

Documentation

See /docs.

Architecture Decision Records

See /docs/adr.

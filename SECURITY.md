# Security Policy

## Supported Status

This project is currently in MVP development.

## Reporting Security Issues

Do not open public issues for sensitive vulnerabilities.

For portfolio/demo purposes, document suspected vulnerabilities privately first, then create a sanitized security issue if appropriate.

## Secrets Policy

- Do not commit real secrets.
- Do not commit `.env`.
- Use `.env.example` for local documentation.
- Use GitHub Secrets for CI/CD secrets when needed.

## Authentication Direction

The platform will use OpenID Connect/OAuth2 through an external identity provider.

Initial local provider:

- Keycloak

## Critical Security Themes

- Tenant isolation
- Company-level authorization
- Idempotency keys for checkout and payments
- No duplicate payments
- No inventory oversell
- Structured audit trails

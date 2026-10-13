# Standalone Anchal Backend — Design Specification

**Date:** 2026-10-13  
**Status:** Proposed  
**Repository:** `backend-anchal`

## 1. Purpose

Extract the Commissioner Monitoring API from the Apna Ranchi backend into an independent ASP.NET service. The new service must preserve the Commissioner Monitoring capabilities while remaining operationally, logically, and data-wise isolated from Apna Ranchi.

The existing Apna Ranchi implementation is a read-only source for this work. No code, routes, database tables, deployment scripts, or configuration will be removed from it.

## 2. Public Routes

| Public path | Owner | Behaviour |
| --- | --- | --- |
| `https://apnaranchi.rsccl.in/anchal/` | Commissioner frontend | Static single-page application |
| `https://apnaranchi.rsccl.in/v1/api/anchal/*` | Anchal API service | Commissioner Monitoring API only |
| Existing Apna Ranchi routes | Existing applications | Unchanged |

Nginx owns the public path routing. The Anchal service listens only on a private loopback port and is never exposed directly to the internet.

## 3. Service Boundary

The new repository contains only the Commissioner Monitoring domain:

- authentication and authorized user roles for Commissioner Monitoring;
- monitoring dashboard, wards, service health, issues, corrections, audit history, and supporting read/write workflows;
- polling/source-ingestion adapters required by that domain;
- Commissioner Monitoring persistence, migrations, seed data, and operational verification;
- API, unit, integration, and operational smoke tests for this service.

It does not contain the Apna Ranchi citizen portal, administrative modules, payment, property, grievance, or other unrelated APIs.

## 4. Data Isolation

- Use an independent Supabase PostgreSQL database connection supplied through environment configuration.
- Use a service-owned EF Core `DbContext`, migrations history, and schema (currently `commissioner_monitoring`).
- Never read from or write to the Apna Ranchi application database.
- Bootstrap environments through the service's EF migrations and deterministic Commissioner Monitoring seed data.
- Development and staging may use Supabase; no credentials are committed to source control.

## 5. Authentication and Browser Security

- The service uses independent JWT issuer, audience, signing key, and user records.
- Authentication cookies are scoped to `Path=/v1/api/anchal`.
- Browser clients use credentialed requests only from an explicit configured origin allow-list.
- CORS supports only configured origins and required request methods/headers; it never uses wildcard origins with credentials.
- API authorization does not share Apna Ranchi sessions or identity configuration.

## 6. Deployment Topology

```text
Browser
  ├─ /anchal/              → Nginx static Commissioner frontend
  ├─ /v1/api/anchal/*      → Nginx → 127.0.0.1:<anchal-port> → PM2 → Anchal ASP.NET API
  └─ all other paths       → existing Apna Ranchi Nginx locations and processes

Anchal API
  └─ independent Supabase PostgreSQL database
```

- The API is published as a self-contained deployment unit and run by a dedicated PM2 process.
- It exposes its own health endpoint for process and deployment verification.
- Nginx configuration adds only dedicated Anchal locations, placed before broader existing locations.
- Restarting, updating, or rolling back the Anchal process does not restart Apna Ranchi services.

## 7. Configuration

Production settings are environment variables or an ignored server-side environment file. The repository provides redacted examples only.

Required groups:

- `ConnectionStrings__CommissionerMonitoring`
- `CommissionerMonitoring__*` database guardrails and source-polling configuration
- `Jwt__*` Anchal-only authentication settings
- `Cors__AllowedOrigins__*`
- `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`, and private service port

Startup must fail clearly when the Anchal database or essential Anchal-only settings are invalid. It must not fall back to an Apna Ranchi connection string.

## 8. Operational Behaviour

- Monitoring data can be refreshed through periodic polling and an authenticated manual refresh action.
- API responses expose retrieval/freshness information required by the frontend.
- Logging identifies the Anchal service and request path without logging credentials or tokens.
- Health checks distinguish process availability from database readiness where practical.
- A deploy verification command checks health, migrations, database ownership guardrails, and key API route availability.

## 9. Migration Approach

1. Inventory and copy only monitoring-specific source from the existing Apna Ranchi worktree.
2. Replace references to broader Apna Ranchi assemblies/namespaces with standalone equivalents.
3. Preserve API contracts at `/v1/api/anchal` so the migrated frontend continues to work.
4. Build and test the standalone service against the isolated Supabase database.
5. Add PM2 and Nginx deployment artifacts with Anchal-only names and locations.
6. Deploy in parallel, validate health and API behaviour, then enable only the Anchal Nginx paths.

## 10. Acceptance Criteria

- Apna Ranchi source and deployed behaviour remain unchanged by this extraction.
- The standalone repository builds and tests independently.
- The service responds through `/v1/api/anchal/*` when mounted by Nginx.
- The service cannot start using the Apna Ranchi database configuration.
- Anchal demo users can authenticate against the independent database.
- Dashboard data, manual refresh, role access, audit history, corrections, and polling work as they did in the embedded implementation.
- Independent deployment, health check, update, and rollback instructions are documented and verified.

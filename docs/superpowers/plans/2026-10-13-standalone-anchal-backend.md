# Standalone Anchal Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the Commissioner Monitoring API as an independently buildable, deployable ASP.NET service mounted only at `/v1/api/anchal`.

**Architecture:** Copy the proven Commissioner Monitoring implementation into four standalone projects—API, Application, Domain, and Infrastructure—renaming its namespaces and removing every dependency on the larger Apna Ranchi system. The API uses its own Supabase-backed EF Core context and JWT/cookie authentication; Nginx maps the public API path to a dedicated PM2-managed loopback process.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10, Npgsql/PostgreSQL (Supabase), JWT bearer authentication, BCrypt, xUnit, PM2, Nginx.

**Spec:** `docs/specs/2026-10-13-standalone-anchal-backend.md`

## Global Constraints

- Copy Commissioner Monitoring code from Apna Ranchi; never delete or alter source in `/Users/swetabh/Developer/CommissionerDashboard/.apna-ranchi-worktrees/commissioner-monitoring-backend`.
- Preserve the public API prefix exactly as `/v1/api/anchal`; do not add `/api/v1/anchal` or another alias.
- Use an independent `ConnectionStrings__CommissionerMonitoring` connection and `commissioner_monitoring` schema only; never fall back to `DefaultConnection`.
- Use independent Anchal JWT issuer, audience, signing key, cookie configuration, users, and roles.
- Keep the API loopback-only; Nginx alone owns the public routing at `https://apnaranchi.rsccl.in/v1/api/anchal/`.
- Do not commit credentials, CA certificates, database URLs, tokens, or server-specific `.env` files.
- Keep the Commissioner frontend outside this repository; it is served independently at `/anchal/`.

## Review Focus

- A misleading `DefaultConnection` must never allow the process to connect to the Apna Ranchi database; Task 2 configuration tests own this.
- A token issued by another issuer/audience or missing Anchal scope must be rejected; Task 3 auth tests own this.
- Credentialed cross-origin browser writes must return CORS headers only for configured origins and reject other origins; Task 4 endpoint tests own this.
- A broader Nginx location must not capture `/v1/api/anchal/*`; Task 6 config validation owns this.
- A process may be live while Supabase is unavailable; `/health` and `/health/commissioner-monitoring` must distinguish this; Task 5 smoke tests own this.

---

## Planned File Structure

| Path | Responsibility |
| --- | --- |
| `Anchal.sln` | Standalone solution with no Apna Ranchi project reference |
| `src/Anchal.Api/` | HTTP host, controllers, CORS/auth middleware, route and health registration |
| `src/Anchal.Application/` | Monitoring DTOs, rules-facing service interface, service implementation, policies |
| `src/Anchal.Domain/` | Monitoring entities and deterministic business rules |
| `src/Anchal.Infrastructure/` | EF Core context/migrations, connection guard, auth implementation, seeding |
| `tests/Anchal.Tests/` | Unit, configuration, authentication, persistence, and endpoint contract tests |
| `deploy/` | Redacted env template, PM2 configuration, Nginx snippet, smoke probe, deployment runbook |

### Task 1: Initialize the independent solution and build boundary

**Files:**
- Create: `Anchal.sln`, `.gitignore`, `README.md`
- Create: `src/Anchal.Api/Anchal.Api.csproj`, `src/Anchal.Application/Anchal.Application.csproj`, `src/Anchal.Domain/Anchal.Domain.csproj`, `src/Anchal.Infrastructure/Anchal.Infrastructure.csproj`
- Create: `tests/Anchal.Tests/Anchal.Tests.csproj`, `tests/Anchal.Tests/Architecture/StandaloneBoundaryTests.cs`

**Interfaces:**
- Produces: solution project graph `Anchal.Api -> {Anchal.Application, Anchal.Infrastructure}`, `Anchal.Infrastructure -> {Anchal.Application, Anchal.Domain}`, `Anchal.Application -> Anchal.Domain`.
- Produces: namespaces rooted at `Anchal.*`; no `Rmc.Ubpms.*` project reference or source import.

- [ ] **Step 1: Write the failing architecture test**

Assert that the API assembly name is `Anchal.Api`, and that the solution/project files do not reference `Rmc.Ubpms`.

- [ ] **Step 2: Run the architecture test to verify it fails**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~StandaloneBoundaryTests`

Expected: FAIL because the standalone solution and test project do not exist.

- [ ] **Step 3: Create the four-project .NET 10 solution and test project**

Use only packages required by Commissioner Monitoring: EF Core/Npgsql, JWT bearer, BCrypt, Serilog, and xUnit test dependencies. Do not copy AWS, email, payment, PDF, upload, or unrelated Apna Ranchi dependencies.

- [ ] **Step 4: Run the architecture test to verify it passes**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~StandaloneBoundaryTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Anchal.sln .gitignore README.md src tests
git commit -m "chore: initialize standalone anchal solution"
```

### Task 2: Copy the monitoring domain, persistence model, and isolated database guard

**Files:**
- Copy/adapt: source `src/Rmc.Ubpms.Domain/CommissionerMonitoring/{MonitoringEntities.cs,Rules/MonitoringRules.cs}` to `src/Anchal.Domain/CommissionerMonitoring/`
- Copy/adapt: source `src/Rmc.Ubpms.Infrastructure/CommissionerMonitoring/Persistence/*` to `src/Anchal.Infrastructure/CommissionerMonitoring/Persistence/`
- Copy/adapt: source migrations `src/Rmc.Ubpms.Infrastructure/Persistence/Migrations/CommissionerMonitoring/*` to `src/Anchal.Infrastructure/CommissionerMonitoring/Persistence/Migrations/`
- Create: `src/Anchal.Infrastructure/CommissionerMonitoring/CommissionerMonitoringRegistration.cs`
- Copy/adapt: `tests/Rmc.Ubpms.Tests/CommissionerMonitoring/{Domain/MonitoringRulesTests.cs,Persistence/CommissionerMonitoringPersistenceTests.cs,Persistence/MonitoringConfigurationTests.cs}`

**Interfaces:**
- Produces: `CommissionerMonitoringDbContext : DbContext`, `CommissionerMonitoringConnectionValidator.Validate(IConfiguration)`, and `IServiceCollection AddCommissionerMonitoringPersistence(IConfiguration, IHostEnvironment)`.
- Produces: schema name `commissioner_monitoring` and the two existing EF migrations with their migration IDs unchanged.

- [ ] **Step 1: Copy and rename the domain-rule tests, then add a failing isolation case**

Add `DedicatedConnectionIsRequiredEvenWhenDefaultConnectionExists`, setting only `ConnectionStrings:DefaultConnection`; assert context resolution throws an error that names `ConnectionStrings:CommissionerMonitoring`.

- [ ] **Step 2: Run the domain and persistence tests to verify they fail**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter "FullyQualifiedName~CommissionerMonitoring.Domain|FullyQualifiedName~CommissionerMonitoring.Persistence"`

Expected: FAIL because the entities, context, and registration do not exist.

- [ ] **Step 3: Copy and adapt the domain entities, rules, EF context, migrations, and connection validator**

Replace all `Rmc.Ubpms.*` namespaces with `Anchal.*`. Retain expected host, port, database, username, environment, and `SSL Mode=VerifyFull` validation. Remove every reference to `ApplicationDbContext`, `DefaultConnection`, and broader infrastructure registration.

- [ ] **Step 4: Run the domain and persistence tests to verify they pass**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter "FullyQualifiedName~CommissionerMonitoring.Domain|FullyQualifiedName~CommissionerMonitoring.Persistence"`

Expected: PASS, including migration model and connection-guard tests.

- [ ] **Step 5: Commit**

```bash
git add src/Anchal.Domain src/Anchal.Infrastructure tests/Anchal.Tests
git commit -m "feat: add isolated commissioner monitoring persistence"
```

### Task 3: Copy application workflows, Anchal authentication, and deterministic provisioning

**Files:**
- Copy/adapt: source `src/Rmc.Ubpms.Application/CommissionerMonitoring/**` to `src/Anchal.Application/CommissionerMonitoring/`
- Copy/adapt: source `src/Rmc.Ubpms.Infrastructure/CommissionerMonitoring/{Auth,Seeding}/**` to `src/Anchal.Infrastructure/CommissionerMonitoring/`
- Create: `src/Anchal.Infrastructure/CommissionerMonitoring/Auth/MonitoringPasswordHasher.cs`
- Copy/adapt: `tests/Rmc.Ubpms.Tests/CommissionerMonitoring/{Application/MonitoringServiceTests.cs,Auth/MonitoringAuthTests.cs}`

**Interfaces:**
- Produces: `IMonitoringService`, `MonitoringService`, `IMonitoringAuthenticator`, `IMonitoringProvisioner`, and `AddMonitoringAuthentication(IServiceCollection)`.
- Consumes: `CommissionerMonitoringDbContext` and `TimeProvider` from Task 2.

- [ ] **Step 1: Copy and rename application/auth tests, then add a failing foreign-token case**

Add `ForeignIssuerOrAudienceTokenCannotAuthorizeAnchal`, asserting a correctly signed token whose issuer or audience is not the configured Anchal value is rejected.

- [ ] **Step 2: Run the application and auth tests to verify they fail**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter "FullyQualifiedName~CommissionerMonitoring.Application|FullyQualifiedName~CommissionerMonitoring.Auth"`

Expected: FAIL because monitoring services, credentials, and policies do not exist.

- [ ] **Step 3: Copy service/DTO/rule workflows and implement standalone JWT/password dependencies**

Use `Anchal:Jwt` (issuer, audience, secret, expiry) as the only token configuration section. Generate JWT claims with the existing monitoring scope, role, user, and office claim names; validate only this issuer/audience. Use BCrypt inside `MonitoringPasswordHasher`; do not reference Apna Ranchi auth interfaces or services.

- [ ] **Step 4: Copy the provisioner and preserve seed behaviour**

Keep deterministic offices, catalog entries, and demo-user provisioning behind explicit environment/configuration invocation; do not provision on arbitrary requests.

- [ ] **Step 5: Run the application and auth tests to verify they pass**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter "FullyQualifiedName~CommissionerMonitoring.Application|FullyQualifiedName~CommissionerMonitoring.Auth"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Anchal.Application src/Anchal.Infrastructure tests/Anchal.Tests
git commit -m "feat: add standalone monitoring workflows and auth"
```

### Task 4: Copy the HTTP contract and secure browser-facing route pipeline

**Files:**
- Copy/adapt: source `src/Rmc.Ubpms.Api/Controllers/CommissionerMonitoring/*` to `src/Anchal.Api/Controllers/`
- Copy/adapt: source `src/Rmc.Ubpms.Api/CommissionerMonitoring/{MonitoringHttpAdapter.cs,CommissionerMonitoringRouteOptions.cs,MonitoringApiAuthorizationHandler.cs,CommissionerMonitoringApiRegistration.cs,service-contract.json}` to `src/Anchal.Api/CommissionerMonitoring/`
- Create: `src/Anchal.Api/Program.cs`, `src/Anchal.Api/appsettings.json`
- Copy/adapt: `tests/Rmc.Ubpms.Tests/CommissionerMonitoring/Api/MonitoringEndpointTests.cs`

**Interfaces:**
- Produces: public endpoints rooted at `CommissionerMonitoringRouteOptions.Prefix == "/v1/api/anchal"`.
- Produces: `POST /login`, session endpoints, office/report entry routes, dashboard/history routes, standardized `{ code, message }` errors, and `X-Monitoring-*` freshness headers.
- Consumes: Task 2 persistence and Task 3 service/auth registrations.

- [ ] **Step 1: Copy and rename endpoint contract tests, then add failing CORS cases**

Add one credentialed `POST /v1/api/anchal/login` test from an allowed origin expecting the same `Access-Control-Allow-Origin` and `Access-Control-Allow-Credentials: true`; add one unlisted-origin test expecting rejection and no credential CORS grant.

- [ ] **Step 2: Run endpoint tests to verify they fail**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~CommissionerMonitoring.Api`

Expected: FAIL because no HTTP host or route pipeline exists.

- [ ] **Step 3: Implement the standalone host and copy the monitoring HTTP adapter/controllers**

Register only monitoring persistence, authentication, authorization, controllers, rate limiting, and CORS. Configure CORS from `Cors:AllowedOrigins`; scope the `rmc_monitoring_session` cookie to `/v1/api/anchal`; retain route gating, no-store responses, anti-cross-site write checks, and JSON error envelopes.

- [ ] **Step 4: Run endpoint tests to verify they pass**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~CommissionerMonitoring.Api`

Expected: PASS, including route-prefix, auth, freshness-header, rate-limit, and CORS cases.

- [ ] **Step 5: Commit**

```bash
git add src/Anchal.Api tests/Anchal.Tests
git commit -m "feat: expose anchal monitoring api contract"
```

### Task 5: Add health, configuration examples, migrations, and automated operational verification

**Files:**
- Create: `deploy/anchal.env.example`, `deploy/apply-migrations.sh`, `deploy/anchal-smoke.py`
- Create: `tests/Anchal.Tests/Api/HealthEndpointTests.cs`
- Modify: `src/Anchal.Api/Program.cs`, `README.md`

**Interfaces:**
- Produces: `GET /health` process health and `GET /health/commissioner-monitoring` database-aware health.
- Produces: a non-mutating smoke command accepting `--base-url`, `--mode enabled`, and an optional bearer token from `ANCHAL_SMOKE_TOKEN`.

- [ ] **Step 1: Write failing health tests**

Assert `/health` remains 200 when the dedicated database cannot be resolved, while `/health/commissioner-monitoring` returns 503 with `{ status: "unhealthy" }`.

- [ ] **Step 2: Run health tests to verify they fail**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~HealthEndpointTests`

Expected: FAIL because standalone health registration is absent.

- [ ] **Step 3: Implement health registration and redacted operational files**

Make `apply-migrations.sh` run only `dotnet ef database update` for `CommissionerMonitoringDbContext`. Make the smoke script read-only, reject credential-bearing URLs, disable proxies/redirects, and never log tokens or connection data.

- [ ] **Step 4: Run health tests and the smoke script against a local test host**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~HealthEndpointTests`

Expected: PASS.

Run: `python3 deploy/anchal-smoke.py --help`

Expected: exit 0 and usage text; no secrets printed.

- [ ] **Step 5: Commit**

```bash
git add deploy src/Anchal.Api tests/Anchal.Tests README.md
git commit -m "feat: add anchal health and operational verification"
```

### Task 6: Package independent PM2 and Nginx deployment assets

**Files:**
- Create: `Dockerfile`, `deploy/ecosystem.anchal.config.cjs`, `deploy/nginx-anchal.conf.template`, `deploy/deploy-anchal.sh`, `deploy/ROLLBACK.md`
- Create: `tests/Anchal.Tests/Deployment/NginxTemplateTests.cs`
- Modify: `README.md`

**Interfaces:**
- Produces: PM2 app name `anchal-api`, loopback `127.0.0.1:8090` by default, and an Anchal-only deployment command.
- Produces: Nginx locations for `/v1/api/anchal/`, `/health/anchal`, and `/anchal/` with no catch-all route changes.

- [ ] **Step 1: Write failing Nginx template tests**

Assert the template has an exact Anchal API location before generic locations, proxies it to loopback port 8090, includes forwarded-host/proto headers, and contains no `/api/` or `/` catch-all location replacement.

- [ ] **Step 2: Run deployment-template tests to verify they fail**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~NginxTemplateTests`

Expected: FAIL because deployment templates do not exist.

- [ ] **Step 3: Add deployment assets and rollback instructions**

The PM2 script must start only `anchal-api`. The deployment script must publish, apply the independent migrations after an explicit operator confirmation, reload only the Anchal PM2 process, run `anchal-smoke.py`, and instruct operators to remove/revert only the Anchal Nginx locations on rollback.

- [ ] **Step 4: Run deployment-template tests and production build**

Run: `dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~NginxTemplateTests`

Expected: PASS.

Run: `dotnet publish src/Anchal.Api/Anchal.Api.csproj -c Release -o /tmp/anchal-publish`

Expected: exit 0.

- [ ] **Step 5: Commit**

```bash
git add Dockerfile deploy tests/Anchal.Tests README.md
git commit -m "feat: add isolated anchal deployment assets"
```

### Task 7: Verify parity against the isolated Supabase environment and prepare handoff

**Files:**
- Create: `deploy/verify-standalone.sh`, `docs/operations/staging-validation.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: configured standalone environment file, independent Supabase credentials, and Anchal API process from Tasks 1–6.
- Produces: repeatable staged validation results without exposing credentials.

- [ ] **Step 1: Write the staging validation checklist**

Include migrations, dedicated-schema verification, health endpoints, demo login, office workflow, commissioner dashboard/history, manual refresh, CORS, and absence of changes to the Apna Ranchi process.

- [ ] **Step 2: Add the verification command**

`verify-standalone.sh` must build/test, run migrations only after `ALLOW_DATABASE_MIGRATION=true`, execute the smoke probe, and redact all environment-derived values from output.

- [ ] **Step 3: Run local full verification**

Run: `dotnet test Anchal.sln`

Expected: all tests PASS.

Run: `dotnet publish src/Anchal.Api/Anchal.Api.csproj -c Release -o /tmp/anchal-publish`

Expected: exit 0.

- [ ] **Step 4: Run staging verification with operator-provided environment configuration**

Run: `ALLOW_DATABASE_MIGRATION=true ./deploy/verify-standalone.sh --base-url https://apnaranchi.rsccl.in`

Expected: migration and smoke checks pass; no Apna Ranchi route or process is restarted.

- [ ] **Step 5: Commit**

```bash
git add deploy docs README.md
git commit -m "docs: add anchal staging verification runbook"
```

## Self-Review

1. **Spec coverage:** Tasks 1–4 cover independent source, API contract, database, auth, and workflows; Task 5 covers health/configuration; Task 6 covers PM2/Nginx isolation and rollback; Task 7 covers deployment validation. No gaps found.
2. **Step scan:** Each implementation task starts with a named failing test, identifies source/target files, gives exact route/configuration names where fixed by the spec, and ends with a focused verification and commit.
3. **Type consistency:** `CommissionerMonitoringDbContext`, `IMonitoringService`, `IMonitoringAuthenticator`, `IMonitoringProvisioner`, `AddCommissionerMonitoringPersistence`, and `AddMonitoringAuthentication` are produced before consuming tasks.
4. **Review focus:** All five failure modes are assigned to Tasks 2–6 and receive a concrete test/check step.
5. **Proportion:** The plan identifies copy sources and interfaces without reproducing implementation bodies; it remains an executable migration guide rather than a code transcript.

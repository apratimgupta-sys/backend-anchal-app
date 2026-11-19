# Field Visit Module — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Serve the field visit module's contract from `backend-anchal`: per-officer accounts, and visits that the server timestamps, verifies and can void.

**Architecture:** Extends the existing CommissionerMonitoring slice rather than adding a parallel one. Officers are `MonitoringUser` rows with a third role; visits are a new entity in the same `DbContext`. Idempotency, auditing, authorization and the Guid-to-Anchal-number mapping all reuse what is there.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core (PostgreSQL), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-18-field-visit-module-design.md` (frontend repo)
**Frontend contract:** `src/shared/types.ts` and `src/client/field-visits.ts` in `CommissionerDashboard`, already written against this.

## Global Constraints

- Run from the solution root: `dotnet test Anchal.sln`. All 89 existing tests stay green.
- Roles are internal strings on `MonitoringUser.Role`: `AnchalOffice`, `Commissioner`, and the new `FieldOfficer`. The wire form is lowercase (`office`, `commissioner`, `officer`), mapped where `SessionDto.Role` is built.
- `officeId` is a Guid inside the service and an Anchal number 1–6 on the wire. Outbound mapping is `MonitoringHttpAdapter.Transform`; inbound is a lookup on `Code == "anchal" + n`, as `Filter` already does.
- Idempotency follows `MonitoringService`: `ValidateRequest(requestId)`, fingerprint, `Replay<T>` over audit events. No new idempotency table.
- Errors use `MonitoringRuleException` / `MonitoringError`; validation failures are 422 with `fieldErrors` keyed `input.<field>`.
- Ward 1–53, Anchal 1–6, service 1–25, Issue/Action at least 20 trimmed characters, visit date is today in IST (`MonitoringRules.Today`).
- Verification thresholds: accuracy ≤ 100 m, capture-to-receipt gap ≤ 30 minutes, absolute.
- Photo columns exist and stay null in v1.

## Review Focus

1. A replayed `requestId` must return the first visit, not create a second.
2. A client-supplied `receivedAt`, `verifiedOnSite` or `officerId` must be ignored, never trusted.
3. An officer must not read another officer's visits through any filter combination.
4. An office account must be refused on every officer and visit route.
5. A 2,000-character multiline Devanagari observation must round-trip byte-identical.

---

### Task B1: Officer role, visit entity, migration

**Files:** `MonitoringEntities.cs`, `MonitoringPolicies.cs`, `CommissionerMonitoringDbContext.cs`, new EF migration, `tests/Anchal.Tests/CommissionerMonitoring/Persistence/`

**Produces:** `MonitoringUser.Designation`, `MonitoringUser.MustChangePassword`, `MonitoringPolicies.FieldOfficerRole`/`FieldOfficer` policy, `MonitoringFieldVisit` entity.

- [ ] Write persistence tests: a field visit round-trips; ward/service bounds are enforced by check constraints; the officer's user row carries designation and the must-change flag.
- [ ] Run them, watch them fail.
- [ ] Add the entity, the two user columns, the role constant and policy, and the `DbContext` configuration.
- [ ] Add the EF migration.
- [ ] Run the tests; run the whole suite.
- [ ] Commit.

### Task B2: Officer administration and password change

**Files:** `MonitoringService` (or a new `OfficerService`), `MonitoringDtos.cs`, new controller, `MonitoringAuthenticator` (officer sign-in gains an office assignment), tests.

**Produces:** `GET/POST /officers`, `PATCH /officers/:id`, `POST /officers/:id/password-reset`, `POST /password`; session carries `officer` and `mustChangePassword`.

- [ ] Write endpoint tests: commissioner-only access; office account refused; create returns a one-time password; deactivation blocks sign-in; `mustChangePassword` gates every other route with 403 `password_change_required`; the temporary password is never returned twice.
- [ ] Run them, watch them fail.
- [ ] Implement the service, DTOs and controller; extend the authenticator so a `FieldOfficer` resolves their home office.
- [ ] Run the tests; run the whole suite.
- [ ] Commit.

### Task B3: Field visit endpoints

**Files:** service, DTOs, new controller, adapter mapping, tests.

**Produces:** `POST /field-visits`, `GET /field-visits`, `POST /field-visits/:id/void`.

- [ ] Write endpoint tests, including every Review Focus item above.
- [ ] Run them, watch them fail.
- [ ] Implement: server sets `receivedAt` and `officerId` and computes `verifiedOnSite`; `visitDate` must be today; replay returns the original row; officers read only their own; commissioner reads all and may void with a reason; voided rows stay in `GET`.
- [ ] Run the tests; run the whole suite.
- [ ] Commit.

### Task B4: End-to-end contract check against the frontend

- [ ] Run the frontend's browser spec against this backend instead of its stubs, or record precisely why it cannot run.
- [ ] Reconcile any shape mismatch in favour of the frontend's `src/shared/types.ts`.
- [ ] Commit.

## Done when

- `dotnet test Anchal.sln` is green, including the new tests.
- Every route in the contract answers with the shapes `src/shared/types.ts` declares.

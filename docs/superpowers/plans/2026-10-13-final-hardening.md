# Final Anchal Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close final release blockers without performing deployment, migration, push, or external actions.

**Architecture:** A non-evaluating dotenv runner passes validated literal values only to child commands. Startup validates configuration but does not connect to the database; readiness does. A provision command composes migration and `IMonitoringProvisioner` as an explicit operator workflow.

**Tech Stack:** .NET 10, EF Core, xUnit, POSIX shell, Python 3.

**Spec:** `docs/specs/2026-10-13-standalone-anchal-backend.md`

## Global Constraints

- Change only this Anchal repository.
- Do not deploy, apply a real migration, push, or perform external actions.
- Use test-first red-green cycles and commit locally.

## Review Focus

- Literal semicolons in a PostgreSQL connection string must survive dotenv parsing.
- Shell metacharacters in dotenv input must never execute.
- Public smoke must reject a healthy response belonging to another app.
- Missing required configuration must fail before the HTTP server starts.
- Provisioning must require all seven account credentials and run only after migrations.

### Task 1: Harden deployment environment handling

**Files:** deployment scripts and deployment tests.

- [ ] Write failing script regressions for safe dotenv parsing and semicolon connection strings.
- [ ] Implement a shared restricted non-evaluating dotenv command runner and use it from deploy and verification scripts.
- [ ] Run focused tests and commit.

### Task 2: Make public health identity-specific and validate startup configuration

**Files:** API startup, authentication/configuration registration, API tests, smoke script/tests.

- [ ] Write failing endpoint and startup tests.
- [ ] Implement startup validation and identity-bearing readiness health; make smoke validate only `/health/anchal`.
- [ ] Run focused tests and commit.

### Task 3: Provide fresh-database provisioning workflow

**Files:** provisioning CLI/script, docs, tests.

- [ ] Write failing tests for command ordering and required credentials.
- [ ] Implement explicit migration-then-provision workflow without automatic execution.
- [ ] Run focused tests, full suite, release publish, write report, and commit.

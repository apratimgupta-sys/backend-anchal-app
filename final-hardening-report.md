# Final Hardening Report

Date: 2026-10-13

## Release blockers addressed

- Deployment no longer sources the Anchal dotenv file. `deploy/anchal-env.py` parses a restricted literal `KEY=value` format and launches child commands with those values, so semicolon-delimited PostgreSQL connection strings remain intact without evaluating shell syntax.
- Public smoke now checks only `/health/anchal` and requires `{ "status": "healthy", "service": "anchal" }`, preventing a different healthy application from satisfying the check.
- Required database target configuration and JWT secret, issuer, audience, and expiry are validated during `Program` application construction immediately before `builder.Build()`. Database connectivity remains exclusively a readiness concern at `/health/commissioner-monitoring` (publicly proxied as `/health/anchal`).
- `deploy/provision-anchal.sh` is the explicit fresh-database workflow. It runs the isolated migration command first, then starts the local `provision --credentials-file` command, which calls `IMonitoringProvisioner`. It is never invoked from ordinary startup or deploy.

## Test-first evidence

- Deployment parser regressions failed before implementation for literal semicolon preservation and command-substitution rejection, then passed.
- Health/startup regressions failed before implementation for absent service identity and deferred required configuration, then passed.
- Provisioning credential/workflow tests failed before implementation, then passed.
- Public smoke routing/identity regression failed before implementation, then passed.
- Construction-time configuration regression proves invalid DB configuration throws before `builder.Build()` can create a hosted server.

## Final verification

Executed locally on 2026-10-13:

```text
dotnet test Anchal.sln --no-restore
Passed: 73, Failed: 0, Skipped: 0

dotnet publish src/Anchal.Api/Anchal.Api.csproj --configuration Release --no-restore --output /private/tmp/anchal-release-publish
Succeeded
```

The test/build output retains pre-existing warnings about redundant Microsoft.Extensions package references and one xUnit analyzer recommendation; neither warning was introduced or changed by this hardening work.

## Local commits

- `6e914d7 fix: harden deploy environment loading`
- `211f928 fix: validate anchal startup and provisioning`

## Operational boundaries honored

No deployment, real migration, push, remote publish, or external service request was performed. The Release publish output is local-only at `/private/tmp/anchal-release-publish`.

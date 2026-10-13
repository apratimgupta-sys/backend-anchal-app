# Task 5 Report: Health and operational verification

## Outcome

Added an anonymous process-health endpoint at `GET /health` and a dedicated database-health endpoint at `GET /health/commissioner-monitoring`. Process health remains available without a resolvable Commissioner Monitoring database; the dedicated endpoint returns HTTP 503 with `{ "status": "unhealthy" }` when that database cannot be configured or reached.

Added the following operational artifacts:

- `deploy/anchal.env.example`: redacted deployment configuration placeholders only.
- `deploy/apply-migrations.sh`: executes only `dotnet ef database update` for `CommissionerMonitoringDbContext`.
- `deploy/anchal-smoke.py`: read-only health verification requiring `--base-url` and `--mode enabled`, with optional bearer authentication from `ANCHAL_SMOKE_TOKEN`. It rejects credential-bearing URLs, disables proxies and redirect following, and does not log tokens or connection details.

The README documents both health contracts, migration use, and the smoke command.

## TDD

- RED: added endpoint integration tests first. With no standalone health registration, both tests failed: `/health` and `/health/commissioner-monitoring` returned HTTP 500.
- GREEN: registered a process-only health endpoint and a scoped database health check that catches unavailable database configuration/connectivity as unhealthy. Health routes skip the normal monitoring authentication/CORS/rate-limit pipeline so readiness can be observed independently.
- The test host supplies only non-sensitive JWT test settings; it intentionally omits the monitoring database configuration.

## Verification

```text
dotnet test tests/Anchal.Tests/Anchal.Tests.csproj --filter FullyQualifiedName~HealthEndpointTests
Passed: 2, Failed: 0

python3 deploy/anchal-smoke.py --help
Exit: 0

dotnet test Anchal.sln
Passed: 58, Failed: 0

git diff --check
Exit: 0
```

The full test suite continues to emit the pre-existing `NU1510` package-pruning warnings and the unrelated `xUnit2010` warning in `MonitoringEndpointTests`; none fail verification.

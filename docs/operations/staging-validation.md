# Standalone Anchal staging validation

Use this runbook after an operator has deployed the independent `anchal-api`
process and installed only the three Anchal Nginx locations. It validates the
standalone environment without restarting, reloading, editing, or otherwise
changing an Apna Ranchi process, route, database, or configuration.

## Preconditions

- Use a protected Anchal-only, shell-compatible environment file with
  independent Supabase credentials. Quote values containing shell punctuation
  such as a PostgreSQL connection string. Do not echo it, copy it into the
  terminal, or commit it.
- Confirm `ConnectionStrings__CommissionerMonitoring` names the isolated
  database and that the guardrail values (`CommissionerMonitoring__Expected*`)
  match it.
- Confirm the service is already running behind the narrow `/v1/api/anchal/`,
  `/health/anchal`, and `/anchal/` Nginx locations. Do not touch generic `/`,
  `/api/`, or existing Apna Ranchi locations.
- Use a staging URL. Do not run this procedure against production without its
  separately approved change process.

## Automated verification

Run from the standalone repository. The verifier loads the protected file only
to configure child commands; it captures and redacts values originating in that
file before printing command output. It does not invoke PM2, Nginx, deployment,
or any Apna Ranchi command.

```sh
export ANCHAL_ENV_FILE=/etc/anchal-api/anchal.env
ALLOW_DATABASE_MIGRATION=true ./deploy/verify-standalone.sh --base-url https://staging.example
```

`ALLOW_DATABASE_MIGRATION=true` is the only value that permits
`deploy/apply-migrations.sh` to run. Without it, the verifier still builds,
tests, and runs the read-only smoke probe but explicitly skips migrations.
`--env-file` may be used instead of `ANCHAL_ENV_FILE` when the operator needs
to select a different protected file.

Review the output for these stages:

- release build and test pass;
- only `CommissionerMonitoringDbContext` migrations run when explicitly
  enabled;
- `/health` and `/health/commissioner-monitoring` both report healthy through
  the read-only smoke probe; and
- no configured value appears in output (redacted values appear as
  `[REDACTED]`).

## Operator checklist

- [ ] Confirm the migration command targets only
  `CommissionerMonitoringDbContext` and completes against the independent
  Supabase connection.
- [ ] Verify the `commissioner_monitoring` schema and its
  `__monitoring_ef_migrations_history` table contain the expected migration
  history; do not inspect or alter Apna Ranchi schemas.
- [ ] Request `/health` and `/health/commissioner-monitoring` through the
  staging route and confirm both return healthy responses.
- [ ] Sign in with an Anchal demo account and confirm its session is scoped to
  `/v1/api/anchal`, not an Apna Ranchi account or cookie.
- [ ] Complete an office workflow appropriate to the test account: view a ward
  or issue, submit an allowed update/correction, and verify the permitted role
  boundary.
- [ ] Open the commissioner dashboard and history/audit view; confirm staged
  data, corrections, and freshness information are visible.
- [ ] Trigger the authenticated manual refresh and confirm the resulting
  freshness timestamp or status changes without restarting a process.
- [ ] From an allowed configured frontend origin, verify credentialed API
  access. From an unlisted origin, verify no credential CORS grant is returned.
- [ ] Confirm only `anchal-api` and the three dedicated Anchal Nginx locations
  were observed. Do not restart, reload, edit, or otherwise change any Apna
  Ranchi process, route, deployment, database, or configuration.

Record the staging URL, verification timestamp, operator, migration decision,
and pass/fail result in the approved change record. Keep credentials, tokens,
connection strings, and copied command output out of that record.

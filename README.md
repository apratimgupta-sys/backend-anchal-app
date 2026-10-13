# Anchal Backend

Independent .NET 10 backend for Commissioner Monitoring.

## Projects

- `Anchal.Api` is the HTTP boundary.
- `Anchal.Application` contains monitoring application workflows.
- `Anchal.Domain` contains monitoring domain types and rules.
- `Anchal.Infrastructure` contains monitoring persistence and integrations.

The solution intentionally has no dependency on Apna Ranchi source projects.

## Operations

Copy `deploy/anchal.env.example` into the deployment system and replace every placeholder through its secret store; do not commit the resulting environment file. Apply only the Commissioner Monitoring migrations from the repository root:

```sh
deploy/apply-migrations.sh
```

`GET /health` is process health and does not require the monitoring database to be resolvable. `GET /health/commissioner-monitoring` checks the dedicated database and returns HTTP 503 with `{ "status": "unhealthy" }` if it cannot be reached or configured safely.

For a fresh isolated database, create a protected credentials file with exactly one 12-128 character password for each of `anchal1` through `anchal6` and `commissioner`, then run the explicit operator command below. It first applies only Anchal migrations and then invokes `IMonitoringProvisioner`; it is never part of ordinary API startup or deployment.

```sh
deploy/provision-anchal.sh --env-file /etc/anchal-api/anchal.env --credentials-file /etc/anchal-api/anchal-provisioning.env
```

After deployment, run the read-only smoke check against the service URL. It follows no redirects, disables proxy use, rejects URLs with credentials, and accepts an optional bearer token only through `ANCHAL_SMOKE_TOKEN`.

```sh
python3 deploy/anchal-smoke.py --base-url https://anchal.example --mode enabled
```

For the staged build/test, optional isolated migration, and smoke workflow, use
the [staging validation runbook](docs/operations/staging-validation.md). The
verifier requires a protected Anchal environment file, redacts values derived
from it, runs migrations only when `ALLOW_DATABASE_MIGRATION=true`, and never
invokes PM2, Nginx, deployment, or Apna Ranchi commands.

## Isolated PM2 and Nginx deployment

`deploy/deploy-anchal.sh` publishes only this service, pauses for the operator
to explicitly confirm the independent Commissioner Monitoring migrations, and
reloads only the `anchal-api` PM2 process on `127.0.0.1:8090`. It requires a
protected Anchal-only dotenv file parsed without shell evaluation plus these variables:

```sh
export ANCHAL_DEPLOY_DIR=/srv/anchal-api
export ANCHAL_BASE_URL=https://apnaranchi.rsccl.in
export ANCHAL_ENV_FILE=/etc/anchal-api/anchal.env
deploy/deploy-anchal.sh
```

Use `deploy/nginx-anchal.conf.template` only as a narrow include within the
existing server block, before broader locations. Replace `__ANCHAL_STATIC_ROOT__`
with the Commissioner frontend's static directory before installation. The
template owns only `/v1/api/anchal/`, `/health/anchal`, and `/anchal/`; it does
not replace generic `/api/` or `/` routing. Review [the rollback instructions](deploy/ROLLBACK.md)
before changing any host configuration.


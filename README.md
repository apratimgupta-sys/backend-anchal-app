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

After deployment, run the read-only smoke check against the service URL. It follows no redirects, disables proxy use, rejects URLs with credentials, and accepts an optional bearer token only through `ANCHAL_SMOKE_TOKEN`.

```sh
python3 deploy/anchal-smoke.py --base-url https://anchal.example --mode enabled
```


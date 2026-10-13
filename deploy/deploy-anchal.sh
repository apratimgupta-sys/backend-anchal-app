#!/usr/bin/env sh
set -eu

: "${ANCHAL_DEPLOY_DIR:?Set ANCHAL_DEPLOY_DIR to the private Anchal release directory.}"
: "${ANCHAL_BASE_URL:?Set ANCHAL_BASE_URL to the public Anchal base URL.}"
: "${ANCHAL_ENV_FILE:?Set ANCHAL_ENV_FILE to the protected Anchal environment file.}"

if [ ! -f "$ANCHAL_ENV_FILE" ]; then
    echo "Anchal environment file was not found: $ANCHAL_ENV_FILE" >&2
    exit 1
fi

release_dir="$ANCHAL_DEPLOY_DIR/releases/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$release_dir"
python3 deploy/anchal-env.py --file "$ANCHAL_ENV_FILE" -- \
    dotnet publish src/Anchal.Api/Anchal.Api.csproj --configuration Release --output "$release_dir"

printf '%s' "Type apply-anchal-migrations to apply only Anchal migrations: "
read -r confirmation
if [ "$confirmation" != "apply-anchal-migrations" ]; then
    echo "Anchal deployment stopped before migrations or PM2 reload."
    exit 1
fi

python3 deploy/anchal-env.py --file "$ANCHAL_ENV_FILE" -- deploy/apply-migrations.sh

python3 deploy/anchal-env.py --file "$ANCHAL_ENV_FILE" --set "ANCHAL_RELEASE_DIR=$release_dir" -- \
    pm2 startOrReload deploy/ecosystem.anchal.config.cjs --only anchal-api --update-env
python3 deploy/anchal-env.py --file "$ANCHAL_ENV_FILE" -- \
    python3 deploy/anchal-smoke.py --base-url "$ANCHAL_BASE_URL" --mode enabled

#!/usr/bin/env sh
set -eu

usage() {
    echo "Usage: deploy/provision-anchal.sh --env-file /protected/anchal.env --credentials-file /protected/anchal-provisioning.env" >&2
}

environment_file="${ANCHAL_ENV_FILE:-}"
credentials_file=""
while [ "$#" -gt 0 ]; do
    case "$1" in
        --env-file) environment_file="${2:?--env-file requires a value}"; shift 2 ;;
        --credentials-file) credentials_file="${2:?--credentials-file requires a value}"; shift 2 ;;
        --help|-h) usage; exit 0 ;;
        *) usage; exit 2 ;;
    esac
done

[ -n "$environment_file" ] && [ -f "$environment_file" ] || { echo "An Anchal environment file is required." >&2; exit 1; }
[ -n "$credentials_file" ] && [ -f "$credentials_file" ] || { echo "A provisioning credentials file is required." >&2; exit 1; }

echo "Applying only Commissioner Monitoring migrations."
python3 deploy/anchal-env.py --file "$environment_file" -- deploy/apply-migrations.sh
echo "Provisioning Anchal accounts and catalog after successful migrations."
python3 deploy/anchal-env.py --file "$environment_file" -- \
    dotnet run --project src/Anchal.Api/Anchal.Api.csproj --no-launch-profile -- \
    provision --credentials-file "$credentials_file"

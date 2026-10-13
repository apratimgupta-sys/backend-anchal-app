#!/usr/bin/env sh
set -eu

usage() {
    cat <<'EOF'
Usage: ALLOW_DATABASE_MIGRATION=true ./deploy/verify-standalone.sh --base-url https://anchal.example [--env-file /protected/anchal.env]

Builds and tests the standalone service, optionally applies only its dedicated
database migrations, then runs the read-only health smoke probe. The configured
environment file is required and command output is redacted before it is shown.
EOF
}

base_url=""
environment_file="${ANCHAL_ENV_FILE:-}"
allow_database_migration="${ALLOW_DATABASE_MIGRATION:-}"

while [ "$#" -gt 0 ]; do
    case "$1" in
        --base-url)
            [ "$#" -ge 2 ] || { echo "--base-url requires a value." >&2; exit 2; }
            base_url="$2"
            shift 2
            ;;
        --env-file)
            [ "$#" -ge 2 ] || { echo "--env-file requires a value." >&2; exit 2; }
            environment_file="$2"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument. Use --help for usage." >&2
            exit 2
            ;;
    esac
done

[ -n "$base_url" ] || { echo "--base-url is required." >&2; exit 2; }
[ -n "$environment_file" ] || { echo "An Anchal environment file is required." >&2; exit 2; }
[ -f "$environment_file" ] || { echo "Anchal environment file was not found." >&2; exit 1; }

temporary_directory=$(mktemp -d "${TMPDIR:-/tmp}/anchal-verify.XXXXXX")
environment_names="$temporary_directory/environment-names"
trap 'rm -rf "$temporary_directory"' EXIT HUP INT TERM

# The protected file must contain shell-compatible KEY=value lines. Record only
# variable names before loading it so values can be removed from child output.
sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' "$environment_file" > "$environment_names"
set -a
. "$environment_file"
set +a

redact_output() {
    python3 -c '
import os
import pathlib
import sys

names = pathlib.Path(sys.argv[1]).read_text().splitlines()
values = sorted(
    {os.environ[name] for name in names if os.environ.get(name)},
    key=len,
    reverse=True,
)
for line in sys.stdin:
    for value in values:
        line = line.replace(value, "[REDACTED]")
    sys.stdout.write(line)
' "$environment_names"
}

run_redacted() {
    command_name="$1"
    shift
    output_file="$temporary_directory/command-output"

    if "$@" > "$output_file" 2>&1; then
        redact_output < "$output_file"
        return 0
    fi

    redact_output < "$output_file" >&2
    echo "$command_name failed." >&2
    return 1
}

echo "Building standalone Anchal solution."
run_redacted "Standalone build" dotnet build Anchal.sln --configuration Release

echo "Testing standalone Anchal solution."
run_redacted "Standalone tests" dotnet test Anchal.sln --no-build

if [ "$allow_database_migration" = "true" ]; then
    echo "Applying only Commissioner Monitoring migrations."
    run_redacted "Commissioner Monitoring migration" deploy/apply-migrations.sh
else
    echo "Skipping database migration; set ALLOW_DATABASE_MIGRATION=true to enable it."
fi

echo "Running read-only Anchal health smoke probe."
run_redacted "Anchal smoke probe" python3 deploy/anchal-smoke.py --base-url "$base_url" --mode enabled

echo "Standalone Anchal verification completed."

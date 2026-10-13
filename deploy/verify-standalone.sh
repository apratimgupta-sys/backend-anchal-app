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
environment_entries="$temporary_directory/environment-entries"
trap 'rm -rf "$temporary_directory"' EXIT HUP INT TERM

parse_environment_file() {
    python3 - "$environment_file" "$environment_entries" <<'PY'
import pathlib
import re
import sys

environment_path = pathlib.Path(sys.argv[1])
destination = pathlib.Path(sys.argv[2])
name_pattern = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")
forbidden_characters = set("&|<>\\`$(){}[]*!~")
entries = []

for line_number, raw_line in enumerate(environment_path.read_text().splitlines(), start=1):
    if not raw_line or raw_line.startswith("#"):
        continue
    if raw_line != raw_line.strip() or "=" not in raw_line:
        print(f"Invalid Anchal environment file line {line_number}.", file=sys.stderr)
        sys.exit(1)

    name, value = raw_line.split("=", 1)
    if not name_pattern.fullmatch(name):
        print(f"Invalid Anchal environment file line {line_number}.", file=sys.stderr)
        sys.exit(1)

    quoted = value[:1] in ("'", '\"')
    if quoted:
        if len(value) < 2 or value[-1] != value[0] or value[0] in value[1:-1]:
            print(f"Invalid Anchal environment file line {line_number}.", file=sys.stderr)
            sys.exit(1)
        value = value[1:-1]
    elif "'" in value or '\"' in value:
        print(f"Invalid Anchal environment file line {line_number}.", file=sys.stderr)
        sys.exit(1)

    if any(character in forbidden_characters for character in value) or (not quoted and ";" in value):
        print(f"Invalid Anchal environment file line {line_number}.", file=sys.stderr)
        sys.exit(1)

    entries.append((name, value))

destination.write_text("".join(f"{name}={value}\n" for name, value in entries))
PY
}

parse_environment_file
sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' "$environment_entries" > "$environment_names"
while IFS= read -r environment_entry; do
    export "$environment_entry"
done < "$environment_entries"

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

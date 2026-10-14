#!/usr/bin/env python3
"""Check an Anchal dotenv file without printing configured values."""

import argparse
import pathlib
import re
import sys


NAME_PATTERN = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")
FORBIDDEN_CHARACTERS = set("&|<>\\`$(){}[]*!~")


def diagnose(path: pathlib.Path) -> str | None:
    for line_number, raw_line in enumerate(path.read_text().splitlines(), start=1):
        if not raw_line or raw_line.startswith("#"):
            continue
        if raw_line != raw_line.strip():
            return f"line {line_number}: leading or trailing whitespace"
        if "=" not in raw_line:
            return f"line {line_number}: missing equals sign"

        name, value = raw_line.split("=", 1)
        if not NAME_PATTERN.fullmatch(name):
            return f"line {line_number}: invalid variable name: {name!r}"
        if value[:1] in ("'", '"'):
            if len(value) < 2 or value[-1] != value[0] or value[0] in value[1:-1]:
                return f"line {line_number}: unterminated quoted value for {name}"
            value = value[1:-1]
        elif "'" in value or '"' in value:
            return f"line {line_number}: unexpected quote in value for {name}"
        if any(character in FORBIDDEN_CHARACTERS for character in value):
            return f"line {line_number}: unsupported shell metacharacter in {name}"
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--file", required=True, type=pathlib.Path)
    args = parser.parse_args()

    if not args.file.is_file():
        print("Anchal environment file was not found.", file=sys.stderr)
        return 1

    problem = diagnose(args.file)
    if problem:
        print(problem, file=sys.stderr)
        return 1

    print("Anchal environment file format is valid.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

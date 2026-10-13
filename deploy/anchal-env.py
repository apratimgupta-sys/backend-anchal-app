#!/usr/bin/env python3
"""Run one command with a restricted, non-evaluating Anchal dotenv file."""

import argparse
import os
import pathlib
import re
import subprocess
import sys


NAME_PATTERN = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")
FORBIDDEN_CHARACTERS = set("&|<>\\`$(){}[]*!~")


def load(path: pathlib.Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line_number, raw_line in enumerate(path.read_text().splitlines(), start=1):
        if not raw_line or raw_line.startswith("#"):
            continue
        if raw_line != raw_line.strip() or "=" not in raw_line:
            raise ValueError(f"Invalid Anchal environment file line {line_number}.")
        name, value = raw_line.split("=", 1)
        if not NAME_PATTERN.fullmatch(name):
            raise ValueError(f"Invalid Anchal environment file line {line_number}.")
        if value[:1] in ("'", '\"'):
            if len(value) < 2 or value[-1] != value[0] or value[0] in value[1:-1]:
                raise ValueError(f"Invalid Anchal environment file line {line_number}.")
            value = value[1:-1]
        elif "'" in value or '\"' in value:
            raise ValueError(f"Invalid Anchal environment file line {line_number}.")
        if any(character in FORBIDDEN_CHARACTERS for character in value):
            raise ValueError(f"Invalid Anchal environment file line {line_number}.")
        values[name] = value
    return values


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--file", required=True, type=pathlib.Path)
    parser.add_argument("--set", action="append", default=[], metavar="NAME=VALUE")
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if not args.file.is_file():
        print("Anchal environment file was not found.", file=sys.stderr)
        return 1
    if not args.command or args.command[0] != "--" or len(args.command) == 1:
        parser.error("a command must follow --")
    try:
        environment = os.environ.copy()
        environment.update(load(args.file))
        for item in args.set:
            name, separator, value = item.partition("=")
            if not separator or not NAME_PATTERN.fullmatch(name):
                raise ValueError("Invalid explicit Anchal environment value.")
            environment[name] = value
    except ValueError as error:
        print(str(error), file=sys.stderr)
        return 1
    return subprocess.run(args.command[1:], env=environment, check=False).returncode


if __name__ == "__main__":
    sys.exit(main())

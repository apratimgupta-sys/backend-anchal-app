#!/usr/bin/env python3
"""Read-only operational health verification for an Anchal deployment."""

import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def parse_base_url(value):
    parsed = urllib.parse.urlsplit(value)
    if parsed.scheme not in ("http", "https") or not parsed.hostname:
        raise argparse.ArgumentTypeError("base URL must be an absolute HTTP(S) URL")
    if parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise argparse.ArgumentTypeError("base URL must not contain credentials, a query, or a fragment")
    return parsed


def health_url(base_url, path):
    return urllib.parse.urlunsplit((base_url.scheme, base_url.netloc, base_url.path.rstrip("/") + path, "", ""))


def check(opener, url, token, expected_status):
    headers = {"Accept": "application/json", "User-Agent": "anchal-smoke/1"}
    if token:
        headers["Authorization"] = "Bearer " + token
    try:
        with opener.open(urllib.request.Request(url, headers=headers, method="GET"), timeout=10) as response:
            body = response.read(4096)
            status = response.status
    except urllib.error.HTTPError as error:
        body = error.read(4096)
        status = error.code
    except urllib.error.URLError:
        raise RuntimeError("health request could not be completed") from None

    if status != expected_status:
        raise RuntimeError("health check returned an unexpected HTTP status")
    try:
        payload = json.loads(body)
    except json.JSONDecodeError:
        raise RuntimeError("health check returned invalid JSON") from None
    if payload.get("status") != "healthy":
        raise RuntimeError("health check did not report healthy")


def main():
    parser = argparse.ArgumentParser(description="Run read-only Anchal health checks.")
    parser.add_argument("--base-url", required=True, type=parse_base_url, help="Deployment HTTP(S) base URL")
    parser.add_argument("--mode", required=True, choices=["enabled"], help="Expected Commissioner Monitoring route mode")
    args = parser.parse_args()

    token = os.environ.get("ANCHAL_SMOKE_TOKEN")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    check(opener, health_url(args.base_url, "/health"), token, 200)
    check(opener, health_url(args.base_url, "/health/commissioner-monitoring"), token, 200)
    print("Anchal health checks passed.")


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print("Anchal smoke check failed: " + str(error), file=sys.stderr)
        sys.exit(1)

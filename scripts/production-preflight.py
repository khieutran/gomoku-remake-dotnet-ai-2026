#!/usr/bin/env python3
"""Validate a production Compose configuration without starting containers."""

import json
import ipaddress
import re
import stat
import subprocess
import sys
from pathlib import Path


def fail(message: str) -> None:
    raise SystemExit(f"FAIL: {message}")


def main() -> None:
    if len(sys.argv) != 2:
        fail("usage: python3 scripts/production-preflight.py /path/to/.env.production")

    env_file = Path(sys.argv[1]).resolve()
    if not env_file.is_file():
        fail(f"environment file does not exist: {env_file}")
    if stat.S_IMODE(env_file.stat().st_mode) & 0o077:
        fail("environment file must not be readable by group or others (chmod 600)")

    repo = Path(__file__).resolve().parent.parent
    result = subprocess.run(
        ["docker", "compose", "--env-file", str(env_file), "-f", str(repo / "compose.production.yaml"),
         "--profile", "migration",
         "config", "--format", "json"],
        cwd=repo, capture_output=True, text=True, check=False,
    )
    if result.returncode:
        fail("docker compose config failed: " + result.stderr.strip())
    config = json.loads(result.stdout)
    services = config["services"]
    api = services["api"]
    proxy = services["proxy"]
    domain = api["environment"]["AllowedHosts"]
    image = api["image"]
    labels = domain.split(".")
    if (len(domain) > 253 or len(labels) < 2
            or any(not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?", label)
                   for label in labels)
            or not re.fullmatch(r"[A-Za-z]{2,}", labels[-1])
            or domain.endswith(".example.com")):
        fail("GOMOKU_DOMAIN must be a real DNS hostname")
    if not re.fullmatch(r"[^\s]+@sha256:[a-f0-9]{64}", image):
        fail("GOMOKU_API_IMAGE must use an immutable sha256 digest")
    if services["migrate"]["image"] != image:
        fail("migration and API must use the same image")
    if api["environment"].get("ASPNETCORE_ENVIRONMENT") != "Production":
        fail("API must run in Production")
    if proxy["environment"].get("GOMOKU_DOMAIN") != domain:
        fail("proxy and API domains differ")
    proxy_ip = proxy["networks"]["front"]["ipv4_address"]
    if api["environment"].get("RateLimiting__KnownProxies__0") != proxy_ip:
        fail("API must trust the exact proxy IP")
    subnet = config["networks"]["front"]["ipam"]["config"][0]["subnet"]
    if ipaddress.ip_address(proxy_ip) not in ipaddress.ip_network(subnet, strict=False):
        fail("proxy IP must belong to the front network subnet")
    for service in ("postgres", "redis", "api", "migrate"):
        if services[service].get("ports"):
            fail(f"{service} must not publish host ports")
    if services["migrate"].get("profiles") != ["migration"]:
        fail("migration must be an explicit profile")

    for name in ("postgres_password", "data_protection_certificate", "data_protection_certificate_password"):
        secret = Path(config["secrets"][name]["file"])
        if not secret.is_file():
            fail(f"{name} secret is missing or empty")
        value = secret.read_bytes().strip()
        if not value:
            fail(f"{name} secret is missing or empty")
        if name != "data_protection_certificate" and len(value) < 24:
            fail(f"{name} secret must have at least 24 bytes")
        if stat.S_IMODE(secret.stat().st_mode) & 0o077:
            fail(f"{name} secret must have mode 600 or stricter")

    print("PASS: production Compose config, private secret, proxy boundary, and image digest")


if __name__ == "__main__":
    main()

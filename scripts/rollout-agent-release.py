#!/usr/bin/env python3
"""Roll out a verified agent through the authenticated, audited support channel."""

import argparse
import http.cookiejar
import json
import os
import socket
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


BASE_URL = "https://uphill-cofounder-trident.ngrok-free.dev"
TARGET_VERSION = "1.6.2"
EXPECTED_BYTES = 76_737_513
BITS_NAME = "MiLifeAgent162"

# The ngrok hostname publishes IPv4 and IPv6 addresses, while this management
# workstation currently has no usable IPv6 route. Keep rollout requests on IPv4.
_getaddrinfo = socket.getaddrinfo
socket.getaddrinfo = lambda *args, **kwargs: [
    item for item in _getaddrinfo(*args, **kwargs) if item[0] == socket.AF_INET
]


class Dashboard:
    def __init__(self, password: str):
        cookies = http.cookiejar.CookieJar()
        self.client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))
        session = self.request("GET", "/device-admin/api/session")
        self.csrf = session["csrfToken"]
        result = self.request("POST", "/device-admin/api/login", {"username": "root", "password": password})
        if not result.get("authenticated"):
            raise RuntimeError("Dashboard authentication failed")

    def request(self, method: str, path: str, body=None):
        data = None if body is None else json.dumps(body).encode()
        headers = {"Accept": "application/json"}
        if data is not None:
            headers["Content-Type"] = "application/json"
            if hasattr(self, "csrf"):
                headers["X-CSRF-TOKEN"] = self.csrf
        request = urllib.request.Request(BASE_URL + path, data=data, headers=headers, method=method)
        try:
            with self.client.open(request, timeout=45) as response:
                content = response.read()
                return json.loads(content) if content else {}
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")
            raise RuntimeError(f"{method} {path} returned {error.code}: {detail}") from error

    def devices(self):
        page = 1
        while True:
            result = self.request("GET", f"/device-admin/api/devices?page={page}")
            yield from result["items"]
            if page * result["pageSize"] >= result["total"]:
                return
            page += 1

    def agents(self, serial: str):
        encoded = urllib.parse.quote(serial, safe="")
        return self.request("GET", f"/device-admin/api/agents?serialNumber={encoded}")["items"]

    def queue(self, agent_id: str, script: str):
        if len(script) > 4096:
            raise RuntimeError(f"Upgrade stage is too large for the command API ({len(script)} characters)")
        return self.request("POST", f"/device-admin/api/agents/{agent_id}/commands", {
            "script": script, "runSilently": True, "deliveryWindowMinutes": 1440
        })

    def history(self, agent_id: str):
        return self.request("GET", f"/device-admin/api/agents/{agent_id}/commands")["items"]


def version_tuple(value: str):
    try:
        return tuple(int(part) for part in value.split("+", 1)[0].split("."))
    except ValueError:
        return (0,)


def wait_for_job(api: Dashboard, agent_id: str, job_id: str, timeout: int = 120):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        item = next((job for job in api.history(agent_id) if job["id"] == job_id), None)
        if item and item["status"] in {"Completed", "Declined", "Expired", "TimedOut", "ResultUnavailable", "Cancelled"}:
            return item
        time.sleep(10)
    raise RuntimeError(f"Command {job_id} did not finish within {timeout} seconds")


def run_command(api: Dashboard, target: dict, script: str, timeout: int = 120):
    queued = api.queue(target["id"], script)
    result = wait_for_job(api, target["id"], queued["id"], timeout)
    if result["status"] != "Completed" or result.get("exitCode") not in (None, 0):
        raise RuntimeError(f"{target['computerName']} command failed: {result['status']} {result.get('output', '')}")
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--execute", action="store_true", help="queue the rollout; otherwise only list targets")
    parser.add_argument("--workspace", default=Path(__file__).resolve().parents[1], type=Path)
    args = parser.parse_args()
    password = os.environ.get("MILIFE_DASHBOARD_PASSWORD")
    if not password:
        raise SystemExit("Set MILIFE_DASHBOARD_PASSWORD")

    api = Dashboard(password)
    targets = []
    for device in api.devices():
        for agent in api.agents(device["serialNumber"]):
            if (agent["availability"] == "Online" and agent["isEnabled"] and not agent["isRevoked"] \
                    and version_tuple(agent["agentVersion"]) < version_tuple(TARGET_VERSION)):
                targets.append({**agent, "serialNumber": device["serialNumber"],
                    "computerName": device.get("computerName") or device["serialNumber"]})

    print(json.dumps({"targetVersion": TARGET_VERSION, "targets": [
        {key: target[key] for key in ("id", "computerName", "serialNumber", "agentVersion")}
        for target in targets]}, indent=2))
    if not args.execute or not targets:
        return

    start_script = (args.workspace / "extre" / "start-agent-1.6-download.ps1").read_text(encoding="utf-8-sig")
    finalize_script = (args.workspace / "extre" / "finalize-agent-1.6.ps1").read_text(encoding="utf-8-sig")
    status_script = ("Import-Module BitsTransfer;$j=Get-BitsTransfer -Name '" + BITS_NAME
        + "' -ErrorAction SilentlyContinue;if(!$j){'Missing'}else{\"$($j.JobState)|$($j.BytesTransferred)|$($j.BytesTotal)\"}")

    active = []
    for target in targets:
        result = run_command(api, target, start_script)
        print(f"{target['computerName']}: {result.get('output', '').strip()}", flush=True)
        active.append(target)

    for target in active:
        deadline = time.monotonic() + 1800
        while True:
            if time.monotonic() >= deadline:
                raise RuntimeError(f"{target['computerName']}: download did not complete within 30 minutes")
            result = run_command(api, target, status_script)
            state = (result.get("output") or "").strip().splitlines()[-1]
            print(f"{target['computerName']}: BITS {state}", flush=True)
            parts = state.split("|")
            if len(parts) == 3 and parts[0] == "Transferred" and int(parts[1]) == EXPECTED_BYTES and int(parts[2]) == EXPECTED_BYTES:
                break
            if parts[0] in {"Error", "TransientError", "Cancelled"}:
                raise RuntimeError(f"{target['computerName']}: BITS entered {parts[0]}")
            time.sleep(30)

        result = run_command(api, target, finalize_script)
        print(f"{target['computerName']}: {result.get('output', '').strip()}", flush=True)

    for target in active:
        deadline = time.monotonic() + 600
        while time.monotonic() < deadline:
            current = next((agent for agent in api.agents(target["serialNumber"]) if agent["id"] == target["id"]), None)
            if current and current["availability"] == "Online" and current["agentVersion"].split("+", 1)[0] == TARGET_VERSION:
                print(f"{target['computerName']}: confirmed online on {TARGET_VERSION}", flush=True)
                break
            time.sleep(15)
        else:
            raise RuntimeError(f"{target['computerName']}: no {TARGET_VERSION} heartbeat after finalization")


if __name__ == "__main__":
    main()

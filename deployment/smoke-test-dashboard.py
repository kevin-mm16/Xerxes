#!/usr/bin/env python3
"""Verify dashboard cookie/CSRF authentication through the loopback gateway."""
from getpass import getpass
from http.cookies import SimpleCookie
import json
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:5089"
cookies: dict[str, str] = {}


def request(path: str, method: str = "GET", body=None, csrf: str | None = None):
    headers = {
        "Host": "uphill-cofounder-trident.ngrok-free.dev",
        "X-Forwarded-Proto": "https",
        "Content-Type": "application/json",
    }
    if cookies:
        headers["Cookie"] = "; ".join(f"{key}={value}" for key, value in cookies.items())
    if csrf:
        headers["X-CSRF-TOKEN"] = csrf
    data = json.dumps(body).encode() if body is not None else None
    response = urllib.request.urlopen(
        urllib.request.Request(BASE + path, data=data, headers=headers, method=method), timeout=15
    )
    for value in response.headers.get_all("Set-Cookie", []):
        parsed = SimpleCookie()
        parsed.load(value)
        for key, morsel in parsed.items():
            cookies[key] = morsel.value
    payload = response.read()
    return response.status, json.loads(payload) if payload else None


password = getpass("Dashboard password: ")
status, session = request("/device-admin/api/session")
assert status == 200 and session["authenticated"] is False and session["csrfToken"]
csrf = session["csrfToken"]

status, logged_in = request(
    "/device-admin/api/login", "POST", {"username": "root", "password": password}, csrf
)
assert status == 200 and logged_in == {"authenticated": True, "username": "root"}

status, session = request("/device-admin/api/session")
assert status == 200 and session["authenticated"] is True and session["username"] == "root"
csrf = session["csrfToken"]

status, devices = request("/device-admin/api/devices?page=1")
assert status == 200 and isinstance(devices["items"], list) and "summary" in devices

status, logged_out = request("/device-admin/api/logout", "POST", {}, csrf)
assert status == 200 and logged_out["authenticated"] is False
print("PASS: dashboard login, CSRF, authenticated device list and logout.")

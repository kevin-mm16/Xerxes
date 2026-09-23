#!/usr/bin/env python3
"""Exercise the deployed API over loopback and MySQL, then delete only this run's synthetic rows.
Run as root on the server. No public requests or real device collection.
"""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import urllib.error
import urllib.request
import uuid

settings = dict(line.split("=", 1) for line in Path("/etc/milife-device-intake/device-intake.env").read_text().splitlines() if "=" in line)
prefix = "DEPLOYTEST-" + uuid.uuid4().hex.upper() + "-"
base = "http://127.0.0.1:5088"
checks = 0

def request(path, data=None, admin=False, token=True, https=True, raw=None):
    headers = {"Content-Type": "application/json"}
    if https: headers["X-Forwarded-Proto"] = "https"
    if token: headers["X-Admin-Token" if admin else "X-Enrollment-Token"] = settings["DEVICE_ADMIN_TOKEN" if admin else "DEVICE_INTAKE_TOKEN"]
    body = raw if raw is not None else (json.dumps(data).encode() if data is not None else None)
    req = urllib.request.Request(base + path, data=body, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=25) as response:
            content = response.read()
            return response.status, json.loads(content) if content else None
    except urllib.error.HTTPError as error:
        content = error.read()
        try: content = json.loads(content)
        except ValueError: content = None
        return error.code, content

def payload(suffix):
    return {"collectionId": str(uuid.uuid4()), "collectorVersion": "1.0.0", "branchCode": "DEPLOYMENT_TEST",
            "serialNumber": prefix + suffix, "computerName": "SYNTHETIC-DEPLOYMENT-TEST",
            "collectedAtUtc": datetime.now(timezone.utc).isoformat()}

def check(condition, message):
    global checks
    if not condition: raise AssertionError(message)
    checks += 1

try:
    check(request("/health")[0] == 200, "health")
    check(request("/api/device-intake", payload("AUTH"), token=False)[0] == 401, "unauthenticated intake")
    check(request("/api/admin/submissions", admin=False)[0] == 401, "admin separation")
    check(request("/api/device-intake", payload("HTTP"), https=False)[0] == 400, "HTTPS required")
    check(request("/api/device-intake", raw=b"{broken")[0] == 400, "malformed JSON")
    check(request("/api/device-intake", raw=b" " * 140000)[0] == 413, "body size")
    invalid = payload("MISSING"); invalid["serialNumber"] = None
    check(request("/api/device-intake", invalid)[0] == 400, "missing serial")
    with ThreadPoolExecutor(max_workers=8) as pool:
        responses = list(pool.map(lambda _: request("/api/device-intake", payload("CONCURRENT")), range(8)))
    check(all(status == 200 for status, body in responses), "concurrent intake status")
    check(sum(not body["isRepeat"] for status, body in responses) == 1, "one device identity")
    check(len({body["submissionId"] for status, body in responses}) == 8, "submission history")
    data = payload("RETRY")
    with ThreadPoolExecutor(max_workers=3) as pool:
        retried = list(pool.map(lambda _: request("/api/device-intake", data), range(3)))
    check(all(status == 200 for status, body in retried), "concurrent retry status")
    check(len({body["submissionId"] for status, body in retried}) == 1, "retry idempotency")
    conflicting = dict(data); conflicting["computerName"] = "CHANGED"
    check(request("/api/device-intake", conflicting)[0] == 409, "conflicting collectionId")
    submission_id = retried[0][1]["submissionId"]
    with ThreadPoolExecutor(max_workers=2) as pool:
        reviews = list(pool.map(lambda state: request(f"/api/admin/submissions/{submission_id}/review",
            {"status":state,"expectedStatus":"PendingReview","note":"Synthetic deployment test"}, admin=True), ["Approved", "Rejected"]))
    check(sorted(status for status, body in reviews) == [200, 409], "concurrent reviews")
    status, detail = request(f"/api/admin/submissions/{submission_id}", admin=True)
    check(status == 200 and len(detail["reviews"]) == 1, "review audit")
    first = request("/api/device-intake", payload("E"))[1]
    second = request("/api/device-intake", payload("É"))[1]
    check(first["isRepeat"] is False and second["isRepeat"] is False, "serial identity collation")
    print(f"PASS: {checks} live MySQL/API checks, including concurrency, review conflicts, auth and validation.")
finally:
    sql = f"""USE milife_device_intake;
DELETE r FROM Reviews r JOIN DeviceSubmissions s ON r.SubmissionId=s.Id WHERE s.SerialNumber LIKE '{prefix}%';
DELETE FROM DeviceSubmissions WHERE SerialNumber LIKE '{prefix}%';
DELETE FROM Devices WHERE SerialNumber LIKE '{prefix}%';
"""
    subprocess.run(["mysql", "--protocol=socket"], input=sql, text=True, check=True, stdout=subprocess.DEVNULL)
    print("Removed this test run's synthetic data only.")

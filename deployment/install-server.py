#!/usr/bin/env python3
"""Install a standalone release and create only its dedicated MySQL schema/user.

Run as root with: install-server.py /absolute/path/to/staging
The staging directory must contain api.tar.gz, server-secrets.json and the service unit.
Secrets are supplied over SSH, never printed or passed in process arguments.
"""
import json
import os
from pathlib import Path
import pwd
import re
import subprocess
import sys
import tarfile

if os.geteuid() != 0:
    raise SystemExit("Run this installer as root.")
staging = Path(sys.argv[1]).resolve(strict=True)
secrets = json.loads((staging / "server-secrets.json").read_text(encoding="utf-8-sig"))
for name in ("IntakeToken", "AdminToken"):
    if not re.fullmatch(r"[a-f0-9]{64}", secrets[name]):
        raise SystemExit("Expected three independent 32-byte hex secrets.")
if not re.fullmatch(r"[a-f0-9]{64}aA1!", secrets["DatabasePassword"]):
    raise SystemExit("Database secret must include the policy-compliant aA1! suffix.")
if len(set(secrets.values())) != 3:
    raise SystemExit("Use distinct secrets.")

def run(*args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)

sql = f"""
CREATE DATABASE IF NOT EXISTS milife_device_intake CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
CREATE USER IF NOT EXISTS 'milife_intake'@'localhost' IDENTIFIED BY '{secrets['DatabasePassword']}';
ALTER USER 'milife_intake'@'localhost' IDENTIFIED BY '{secrets['DatabasePassword']}';
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES, DROP
ON milife_device_intake.* TO 'milife_intake'@'localhost';
"""
run("mysql", "--protocol=socket", input=sql, text=True, stdout=subprocess.DEVNULL)
try:
    pwd.getpwnam("milife-intake")
except KeyError:
    run("useradd", "--system", "--home", "/var/lib/milife-device-intake", "--shell", "/usr/sbin/nologin", "milife-intake")

destination = Path("/opt/milife-device-intake")
destination.mkdir(mode=0o755, parents=True, exist_ok=True)
with tarfile.open(staging / "api.tar.gz") as archive:
    archive.extractall(destination, filter="data")
destination.chmod(0o755)
for item in destination.rglob("*"):
    if item.is_file():
        item.chmod(0o644)
    elif item.is_dir():
        item.chmod(0o755)
(destination / "downloads").mkdir(mode=0o755, exist_ok=True)
(destination / "downloads").chmod(0o755)
config_directory = Path("/etc/milife-device-intake")
config_directory.mkdir(mode=0o700, parents=True, exist_ok=True)
environment = config_directory / "device-intake.env"
environment.write_text(
    f"DEVICE_INTAKE_TOKEN={secrets['IntakeToken']}\n"
    f"DEVICE_ADMIN_TOKEN={secrets['AdminToken']}\n"
    "ASPNETCORE_ENVIRONMENT=Production\n"
    "ASPNETCORE_URLS=http://127.0.0.1:5088\n"
    "Database__Provider=MySql\nDatabase__MySqlVersion=8.4.11\n"
    f'ConnectionStrings__DeviceIntake="Server=127.0.0.1;Port=3306;Database=milife_device_intake;User=milife_intake;Password={secrets["DatabasePassword"]};Connection Timeout=10"\n'
    "Intake__CollectorPath=/opt/milife-device-intake/downloads/MiLifeDeviceCollector.exe\n"
    "Intake__RequestsPerMinute=60\nIntake__GlobalRequestsPerMinute=300\n"
)
environment.chmod(0o600)
run("install", "-m", "0644", str(staging / "milife-device-intake.service"), "/etc/systemd/system/milife-device-intake.service")
run("systemctl", "daemon-reload")
run("systemctl", "enable", "--now", "milife-device-intake.service")
print("Standalone MySQL database/user and systemd service installed. Verify health and then restrict runtime grants.")

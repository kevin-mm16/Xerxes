#!/usr/bin/env python3
"""Deploy release 1.5 with an additive migration and a rollback backup."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import time
import urllib.request

if os.geteuid() != 0:
    raise SystemExit("Run as root")

stage = Path("/home/mitech/milife-1.5")
release = Path("/opt/milife-device-intake")
environment = Path("/etc/milife-device-intake/device-intake.env")
archive = stage / "api.tar.gz"
agent = stage / "MiLifeDeviceAgent.exe"
backup_script = stage / "backup-device-intake.sh"
backup_unit = stage / "milife-device-intake-backup.service"
installed_agent = release / "downloads" / "MiLifeDeviceAgent.exe"
installed_backup_script = Path("/usr/local/sbin/backup-milife-device-intake")
installed_backup_unit = Path("/etc/systemd/system/milife-device-intake-backup.service")
backup = Path("/var/backups/milife-promotion") / time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())

for required in (archive, agent, backup_script, backup_unit, environment, installed_agent,
                 installed_backup_script, installed_backup_unit):
    if not required.is_file():
        raise SystemExit(f"Missing required file: {required}")
if release.resolve() != release or release.is_symlink():
    raise SystemExit("Unexpected release directory")

subprocess.run(["bash", "-n", str(backup_script)], check=True)
backup.mkdir(parents=True, mode=0o700)
subprocess.run(["mysqldump", "--single-transaction", "--no-tablespaces", "milife_device_intake"],
    stdout=(backup / "database.sql").open("wb"), check=True)
subprocess.run(["tar", "--exclude=downloads", "--exclude=.keys", "-czf", str(backup / "api.tar.gz"),
    "-C", str(release), "."], check=True)
for source, name in ((environment, "device-intake.env"), (installed_agent, "MiLifeDeviceAgent.exe"),
                     (installed_backup_script, "backup-device-intake.sh"),
                     (installed_backup_unit, "milife-device-intake-backup.service")):
    shutil.copy2(source, backup / name)

def clear_application():
    for item in release.iterdir():
        if item.name in {"downloads", ".keys"}:
            continue
        if item.is_symlink() or item.is_file():
            item.unlink()
        elif item.is_dir():
            shutil.rmtree(item)

def extract_checked(source: Path, destination: Path):
    root = destination.resolve()
    with tarfile.open(source) as package:
        for member in package.getmembers():
            if member.issym() or member.islnk() or member.isdev():
                raise RuntimeError("Archive contains an unsupported entry")
            target = (destination / member.name).resolve()
            if root != target and root not in target.parents:
                raise RuntimeError("Archive contains an unsafe path")
        package.extractall(destination)

def wait_for_health():
    for _ in range(45):
        try:
            with urllib.request.urlopen("http://127.0.0.1:5088/health", timeout=2) as response:
                if json.load(response).get("status") == "healthy":
                    return
        except Exception:
            time.sleep(1)
    raise RuntimeError("API did not become healthy")

ddl = "GRANT CREATE, ALTER, INDEX, REFERENCES, DROP ON milife_device_intake.* TO 'milife_intake'@'localhost'; FLUSH PRIVILEGES;"
revoke = "REVOKE CREATE, ALTER, INDEX, REFERENCES, DROP ON milife_device_intake.* FROM 'milife_intake'@'localhost'; FLUSH PRIVILEGES;"
subprocess.run(["mysql"], input=ddl, text=True, check=True)
try:
    subprocess.run(["systemctl", "stop", "milife-device-intake.service"], check=True)
    clear_application()
    extract_checked(archive, release)
    for item in release.rglob("*"):
        item.chmod(0o755 if item.is_dir() else 0o644)
    (release / "MiLife.DeviceIntake.Api").chmod(0o755)
    shutil.copy2(agent, installed_agent)
    installed_agent.chmod(0o644)
    shutil.copy2(backup_script, installed_backup_script)
    installed_backup_script.chmod(0o755)
    shutil.copy2(backup_unit, installed_backup_unit)
    installed_backup_unit.chmod(0o644)
    subprocess.run(["systemctl", "daemon-reload"], check=True)
    subprocess.run(["systemctl", "start", "milife-device-intake.service"], check=True)
    wait_for_health()
    subprocess.run(["systemctl", "start", "milife-device-intake-backup.service"], check=True)
    marker = Path("/var/lib/milife-device-intake/last-backup.utc")
    if not marker.is_file() or not marker.read_text().strip():
        raise RuntimeError("Backup health marker was not created")
except Exception:
    subprocess.run(["systemctl", "stop", "milife-device-intake.service"], check=False)
    clear_application()
    extract_checked(backup / "api.tar.gz", release)
    shutil.copy2(backup / "device-intake.env", environment)
    shutil.copy2(backup / "MiLifeDeviceAgent.exe", installed_agent)
    shutil.copy2(backup / "backup-device-intake.sh", installed_backup_script)
    shutil.copy2(backup / "milife-device-intake-backup.service", installed_backup_unit)
    installed_backup_script.chmod(0o755)
    subprocess.run(["systemctl", "daemon-reload"], check=False)
    subprocess.run(["systemctl", "start", "milife-device-intake.service"], check=False)
    raise
finally:
    subprocess.run(["mysql"], input=revoke, text=True, check=True)

print("RELEASE_1_5_READY")
print("Backup:", backup)

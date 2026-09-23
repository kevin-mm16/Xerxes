#!/usr/bin/env python3
"""Deploy the isolated 1.4 preview while preserving production URLs and downloads."""
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

stage = Path("/home/mitech/milife-preview")
release = Path("/opt/milife-device-intake")
environment = Path("/etc/milife-device-intake/device-intake.env")
nginx_config = Path("/etc/nginx/sites-available/milife-device-intake")
backup = Path("/var/backups/milife-preview") / time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
archive = stage / "preview-api.tar.gz"
preview_agent = stage / "MiLifeDeviceAgentPreview.exe"
preview_nginx = stage / "milife-dedicated.nginx.conf"

for required in (archive, preview_agent, preview_nginx, environment, nginx_config):
    if not required.is_file():
        raise SystemExit(f"Missing required file: {required}")
if release.resolve() != release or release.is_symlink():
    raise SystemExit("Unexpected release directory")

backup.mkdir(parents=True, mode=0o700)
subprocess.run(["mysqldump", "--single-transaction", "--no-tablespaces", "milife_device_intake"],
    stdout=(backup / "database.sql").open("wb"), check=True)
subprocess.run(["tar", "--exclude=downloads", "--exclude=.keys", "-czf", str(backup / "api.tar.gz"), "-C", str(release), "."], check=True)
shutil.copy2(environment, backup / "device-intake.env")
shutil.copy2(nginx_config, backup / "milife-device-intake.nginx.conf")

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

def set_environment_value(key: str, value: str):
    lines = environment.read_text().splitlines()
    lines = [line for line in lines if line.partition("=")[0] != key]
    environment.write_text("\n".join(lines + [f"{key}={value}"]) + "\n")
    environment.chmod(0o640)

def wait_for_health():
    for _ in range(45):
        try:
            with urllib.request.urlopen("http://127.0.0.1:5088/health", timeout=2) as response:
                if json.load(response).get("status") == "healthy":
                    return
        except Exception:
            time.sleep(1)
    raise RuntimeError("Preview API did not become healthy")

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
    (release / "downloads").mkdir(exist_ok=True, mode=0o755)
    shutil.copy2(preview_agent, release / "downloads" / "MiLifeDeviceAgentPreview.exe")
    (release / "downloads" / "MiLifeDeviceAgentPreview.exe").chmod(0o644)
    set_environment_value("Intake__PreviewAgentPath", "/opt/milife-device-intake/downloads/MiLifeDeviceAgentPreview.exe")
    shutil.copy2(preview_nginx, nginx_config)
    nginx_config.chmod(0o644)
    subprocess.run(["nginx", "-t"], check=True)
    subprocess.run(["systemctl", "start", "milife-device-intake.service"], check=True)
    wait_for_health()
    subprocess.run(["systemctl", "reload", "nginx"], check=True)
except Exception:
    subprocess.run(["systemctl", "stop", "milife-device-intake.service"], check=False)
    clear_application()
    extract_checked(backup / "api.tar.gz", release)
    shutil.copy2(backup / "device-intake.env", environment)
    shutil.copy2(backup / "milife-device-intake.nginx.conf", nginx_config)
    subprocess.run(["systemctl", "start", "milife-device-intake.service"], check=False)
    subprocess.run(["nginx", "-t"], check=False)
    subprocess.run(["systemctl", "reload", "nginx"], check=False)
    raise
finally:
    subprocess.run(["mysql"], input=revoke, text=True, check=True)

print("PREVIEW_READY")
print("Backup:", backup)

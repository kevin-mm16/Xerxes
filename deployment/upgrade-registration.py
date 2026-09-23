#!/usr/bin/env python3
"""Deploy registration management; preserve the existing inventory application."""
import json, os, shutil, subprocess, tarfile, time, urllib.request
from pathlib import Path
if os.geteuid() != 0: raise SystemExit('Run as root')
stage=Path('/home/kevin-x/milife-deploy')
release=Path('/opt/milife-device-intake')
backup=stage/('before-registration-'+str(int(time.time())))
backup.mkdir(mode=0o700)
with (backup/'database.sql').open('wb') as out:
    subprocess.run(['mysqldump','--single-transaction','--no-tablespaces','milife_device_intake'],stdout=out,check=True)
subprocess.run(['tar','--exclude=downloads','--exclude=.keys','-czf',str(backup/'api.tar.gz'),'-C',str(release),'.'],check=True)
shutil.copy2(release/'downloads/MiLifeDeviceAgent.exe',backup/'MiLifeDeviceAgent.exe')
try:
    subprocess.run(['mysql'],input="GRANT CREATE, ALTER, INDEX, REFERENCES, DROP ON milife_device_intake.* TO 'milife_intake'@'localhost';",text=True,check=True)
    subprocess.run(['systemctl','stop','milife-device-intake'],check=True)
    with tarfile.open(stage/'registration-api.tar.gz') as archive: archive.extractall(release,filter='data')
    for item in release.rglob('*'):
        if item.is_dir(): item.chmod(0o755)
        elif item.is_file(): item.chmod(0o644)
    subprocess.run(['install','-m','0644',str(stage/'MiLifeDeviceAgent.exe'),str(release/'downloads/MiLifeDeviceAgent.exe')],check=True)
    subprocess.run(['systemctl','start','milife-device-intake'],check=True)
    for attempt in range(30):
        try:
            with urllib.request.urlopen('http://127.0.0.1:5088/health',timeout=2) as response:
                if json.load(response)['status']=='healthy': break
        except Exception: time.sleep(1)
    else: raise RuntimeError('API failed readiness; restore API/download from backup and inspect migration journal.')
finally:
    subprocess.run(['mysql'],input="REVOKE CREATE, ALTER, INDEX, REFERENCES, DROP ON milife_device_intake.* FROM 'milife_intake'@'localhost';",text=True,check=True)
print('Registration update healthy. Backup:',backup)

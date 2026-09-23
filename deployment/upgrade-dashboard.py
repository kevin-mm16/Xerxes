#!/usr/bin/env python3
"""Apply the dashboard/heartbeat release to the existing standalone installation."""
import json
import os
from pathlib import Path
import subprocess
import tarfile
import time
import urllib.request

if os.geteuid() != 0: raise SystemExit("Run as root")
stage = Path('/home/kevin-x/milife-deploy')
release = Path('/opt/milife-device-intake')
stamp = str(int(time.time()))
backup = stage / ('before-dashboard-' + stamp)
backup.mkdir(mode=0o700)
with (backup / 'database.sql').open('wb') as output:
    subprocess.run(['mysqldump','--single-transaction','--no-tablespaces','milife_device_intake'],stdout=output,check=True)
subprocess.run(['tar','--exclude=downloads','--exclude=.keys','-czf',str(backup/'api.tar.gz'),'-C',str(release),'.'],check=True)
environment = Path('/etc/milife-device-intake/device-intake.env')
(backup/'device-intake.env').write_bytes(environment.read_bytes())
secrets = json.loads((stage/'dashboard-secrets.json').read_text(encoding='utf-8-sig'))
assert secrets['Username']=='root' and secrets['PasswordHash'].startswith('v1.600000.')
values = {'DEVICE_ADMIN_USERNAME':secrets['Username'],'DEVICE_ADMIN_PASSWORD_HASH':secrets['PasswordHash'],
          'Admin__DataProtectionPath':'/var/lib/milife-device-intake/keys',
          'Intake__AgentPath':'/opt/milife-device-intake/downloads/MiLifeDeviceAgent.exe'}
lines = [line for line in environment.read_text().splitlines() if line.partition('=')[0] not in values]
environment.write_text('\n'.join(lines + [k+'='+v for k,v in values.items()])+'\n')
environment.chmod(0o600)
subprocess.run(['mysql'],input="GRANT CREATE, ALTER, INDEX, REFERENCES, DROP ON milife_device_intake.* TO 'milife_intake'@'localhost';",text=True,check=True)
subprocess.run(['systemctl','stop','milife-device-intake'],check=True)
with tarfile.open(stage/'dashboard-api.tar.gz') as archive: archive.extractall(release,filter='data')
release.chmod(0o755)
for item in release.rglob('*'):
    if item.is_dir(): item.chmod(0o755)
    elif item.is_file(): item.chmod(0o644)
subprocess.run(['systemctl','start','milife-device-intake'],check=True)
ready=False
for _ in range(30):
    try:
        with urllib.request.urlopen('http://127.0.0.1:5088/health',timeout=2) as response:
            if json.load(response)['status']=='healthy': ready=True; break
    except Exception: time.sleep(1)
if not ready: raise SystemExit('API did not become healthy; inspect journal. Backup: '+str(backup))
subprocess.run(['mysql'],input="REVOKE CREATE, ALTER, INDEX, REFERENCES, DROP ON milife_device_intake.* FROM 'milife_intake'@'localhost';",text=True,check=True)
subprocess.run(['install','-m','0644',str(stage/'milife-gateway.nginx.conf'),'/etc/nginx/sites-available/milife-gateway'],check=True)
subprocess.run(['nginx','-t'],check=True)
subprocess.run(['systemctl','reload','nginx'],check=True)
print('Dashboard and heartbeat API deployed. Backup:',backup)

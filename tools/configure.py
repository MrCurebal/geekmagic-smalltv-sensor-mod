#!/usr/bin/env python3
"""Generate local Wi-Fi credentials and a shared random device key. No secrets are printed."""
import getpass
import json
import pathlib
import secrets

root = pathlib.Path(__file__).resolve().parents[1]
ssid = input('2.4 GHz Wi-Fi name: ').strip()
password = getpass.getpass('Wi-Fi password: ')
address = input('Display URL [http://192.168.1.100/]: ').strip() or 'http://192.168.1.100/'
if not 1 <= len(ssid.encode()) <= 32 or not 8 <= len(password) <= 63:
    raise SystemExit('Use a valid SSID and an 8-63 character WPA2 password.')
key = secrets.token_hex(24)
header = '#pragma once\n' + '\n'.join('#define ' + name + ' ' + json.dumps(value) for name, value in (
    ('LOADER_SSID', ssid), ('LOADER_PASS', password), ('API_KEY', key))) + '\n'
(root / 'firmware/include/private_config.h').write_text(header, encoding='utf-8')
config = {'Device': address.rstrip('/') + '/', 'ApiKey': key, 'IntervalSeconds': 2, 'Brightness': 32, 'TaskName': 'GeekMagicSensorSender'}
for folder in (root / 'dist/windows', root / 'clients/linux'):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / 'config.json').write_text(json.dumps(config, indent=2), encoding='utf-8')
print('Created ignored private_config.h and config.json files. Keep these files and built firmware private.')

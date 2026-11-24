#!/usr/bin/env python3
"""Send real Linux hwmon/proc metrics to the native SmallTV display. No dependencies."""
import json
import pathlib
import signal
import sys
import time
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent
CONFIG = json.loads((ROOT / 'config.json').read_text(encoding='utf-8-sig'))
URL = CONFIG['Device'].rstrip('/') + '/api/metrics'
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
RUNNING = True

def stop(*_):
    global RUNNING
    RUNNING = False

signal.signal(signal.SIGTERM, stop)
signal.signal(signal.SIGINT, stop)

def number(path):
    try:
        return float(pathlib.Path(path).read_text().strip())
    except (OSError, ValueError):
        return None

def temperature(hw, labels):
    for label in labels:
        for item in sorted(hw.glob('temp*_input')):
            label_path = item.with_name(item.name.replace('_input', '_label'))
            try:
                if label_path.read_text().strip().lower() != label.lower():
                    continue
            except OSError:
                continue
            value = number(item)
            if value is not None and 0 < value < 125000:
                return value / 1000
    value = number(hw / 'temp1_input')
    return value / 1000 if value is not None and 0 < value < 125000 else None

def hardware():
    cpu, gpu, board = None, None, None
    for hw in sorted(pathlib.Path('/sys/class/hwmon').glob('hwmon*')):
        try:
            name = (hw / 'name').read_text().strip()
        except OSError:
            continue
        if name in ('k10temp', 'zenpower', 'coretemp') and cpu is None:
            cpu = hw
        if name == 'amdgpu' and gpu is None:
            gpu = hw
        if name.startswith('it8689') and board is None:
            board = hw
    return cpu, gpu, board

def cpu_counters():
    values = [int(x) for x in pathlib.Path('/proc/stat').read_text().splitlines()[0].split()[1:9]]
    # guest/guest_nice already belong to user/nice; do not count them twice.
    return sum(values), values[3] + values[4]

def ram():
    values = {}
    for row in pathlib.Path('/proc/meminfo').read_text().splitlines():
        name, value = row.split(':', 1)
        values[name] = int(value.strip().split()[0])
    total = values['MemTotal']
    used = total - values['MemAvailable']
    return used * 100 / total, used / 1048576, total / 1048576

def main():
    cpu, gpu, board = hardware()
    before = cpu_counters()
    time.sleep(1)
    while RUNNING:
        started = time.monotonic()
        after = cpu_counters()
        delta = after[0] - before[0]
        load = 100 * (1 - (after[1] - before[1]) / delta) if delta > 0 else None
        before = after
        ram_load, used, total = ram()
        payload = {
            'CaseTemp': temperature(board, ('System #1', 'System1', 'SYSTIN')) if board else None,
            'CpuTemp': temperature(cpu, ('Tctl', 'Tdie', 'Package id 0')) if cpu else None,
            'CpuLoad': round(max(0, min(100, load)), 2) if load is not None else None,
            'GpuTemp': temperature(gpu, ('edge',)) if gpu else None,
            'GpuLoad': number(gpu / 'device' / 'gpu_busy_percent') if gpu else None,
            'RamLoad': round(ram_load, 2), 'RamUsed': round(used, 2), 'RamTotal': round(total, 2),
        }
        try:
            request = urllib.request.Request(URL, data=json.dumps(payload, allow_nan=False).encode(),
                headers={'Content-Type': 'application/json', 'X-Api-Key': CONFIG['ApiKey']}, method='POST')
            with OPENER.open(request, timeout=5) as response:
                if json.load(response).get('ok') is not True:
                    raise RuntimeError('Device rejected data')
            print(time.strftime('%H:%M:%S'), 'OK', json.dumps(payload), flush=True)
        except (OSError, ValueError, RuntimeError) as error:
            print(time.strftime('%H:%M:%S'), 'Connection:', error, file=sys.stderr, flush=True)
        remaining = max(0, CONFIG.get('IntervalSeconds', 2) - (time.monotonic() - started))
        deadline = time.monotonic() + remaining
        while RUNNING and time.monotonic() < deadline:
            time.sleep(min(.25, max(0, deadline - time.monotonic())))

if __name__ == '__main__':
    main()

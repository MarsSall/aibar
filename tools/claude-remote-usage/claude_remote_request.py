#!/usr/bin/env python3
"""Watch for AIBar refresh requests on Windows and start the existing collector service."""

import argparse
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time

import claude_remote_usage as quota

FILENAME = 'claude-refresh-request.json'
SERVICE = 'aibar-claude-remote-usage.service'
INTERVAL_SECONDS = 15
# Explicit requests add Anthropic queries on top of the five-minute timer; bound them.
MIN_QUERY_SPACING = timedelta(seconds=60)
# A request older than this (e.g. left over while Linux was off) is not worth a query.
MAX_REQUEST_AGE = timedelta(minutes=10)
MAX_CLOCK_SKEW = timedelta(minutes=2)
MAX_BYTES = 4096
STAMP = re.compile(r'\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?Z\Z')


@dataclass
class State:
    served: str | None = None
    last_query: datetime | None = None


def parse_request(data):
    if len(data) > MAX_BYTES:
        return None
    try:
        value = json.loads(data.decode('utf-8'))
    except (UnicodeDecodeError, ValueError):
        return None
    if not isinstance(value, dict) or list(value) != ['schemaVersion', 'requestedAt']:
        return None
    stamp = value['requestedAt']
    if value['schemaVersion'] != 1 or type(value['schemaVersion']) is not int or \
            not isinstance(stamp, str) or not STAMP.fullmatch(stamp):
        return None
    try:
        return stamp, datetime.fromisoformat(stamp[:19] + '+00:00')
    except ValueError:
        return None


def ssh_options(control_dir):
    # One master connection serves every 15-second check while the watcher runs.
    return ['-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10', '-o', 'ControlMaster=auto',
            '-o', 'ControlPath=' + str(Path(control_dir) / 'aibar-ssh-%C'), '-o', 'ControlPersist=120']


def read_request(host, remote_dir, runner, control_dir):
    command = ('powershell.exe -NoProfile -NonInteractive -Command "'
               "$p = Join-Path $HOME '" + remote_dir + '/' + FILENAME + "'; "
               "if (Test-Path -LiteralPath $p -PathType Leaf) { [System.IO.File]::ReadAllText($p) }\"")
    result = runner(['ssh', *ssh_options(control_dir), host, command], check=True, stdin=subprocess.DEVNULL,
                    stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30)
    return (result.stdout or b'').strip()


def check(config_path, state, now, *, runner=None, control_dir=None):
    """Return none, requested, throttled, unreachable or failed; never raises for remote state."""
    runner = runner or subprocess.run
    host, remote_dir = quota.read_config(config_path)
    try:
        request = parse_request(read_request(host, remote_dir, runner, control_dir or default_control_dir()))
    except (OSError, subprocess.SubprocessError):
        return 'unreachable'
    if request is None or request[0] == state.served:
        return 'none'
    stamp, requested_at = request
    if not now - MAX_REQUEST_AGE <= requested_at <= now + MAX_CLOCK_SKEW:
        return 'none'
    if state.last_query is not None and now - state.last_query < MIN_QUERY_SPACING:
        return 'throttled'
    state.last_query = now
    try:
        # systemd serializes this with the timer: the collector never runs concurrently.
        runner(['systemctl', '--user', 'start', SERVICE], check=True, stdin=subprocess.DEVNULL,
               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120)
    except (OSError, subprocess.SubprocessError):
        return 'failed'
    state.served = stamp
    return 'requested'


def default_control_dir():
    return os.environ.get('XDG_RUNTIME_DIR', '').strip() or tempfile.gettempdir()


def watch(config_path, *, runner=None, control_dir=None, now=None, sleep=time.sleep, iterations=None):
    state = State()
    now = now or (lambda: datetime.now(timezone.utc))
    count = 0
    while iterations is None or count < iterations:
        count += 1
        outcome = check(config_path, state, now(), runner=runner, control_dir=control_dir)
        if outcome in ('requested', 'failed'):
            # State-only log line; never paths, hosts or remote content.
            print(outcome + ' ' + (state.served or '-'), file=sys.stderr, flush=True)
        sleep(INTERVAL_SECONDS)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--once', action='store_true', help='check for one request and exit')
    args = parser.parse_args(argv)
    config = Path.home() / '.config/aibar/claude-remote-usage.conf'
    try:
        if args.once:
            print(check(config, State(), datetime.now(timezone.utc)), file=sys.stderr)
        else:
            watch(config)
    except KeyboardInterrupt:
        return 0
    except Exception:
        print('unavailable', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())

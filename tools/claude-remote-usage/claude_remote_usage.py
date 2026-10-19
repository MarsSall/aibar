#!/usr/bin/env python3
"""Read-only Claude usage collector; only the normalized contract leaves this host."""

import argparse
from datetime import datetime, timezone
import http.client
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from urllib.error import HTTPError
from urllib.request import HTTPRedirectHandler, Request, build_opener

ENDPOINT = 'https://api.anthropic.com/api/oauth/usage'
FILENAME = 'claude-remote-usage.json'


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate field')
        result[key] = value
    return result


def utc_stamp(value):
    return value.astimezone(timezone.utc).isoformat().replace('+00:00', 'Z')


def document(now, state, five_hour=None, weekly=None):
    return dict(schemaVersion=1, generatedAt=utc_stamp(now), state=state,
                fiveHour=five_hour if state == 'current' else None,
                weekly=weekly if state == 'current' else None)


def window(value):
    if value is None:
        return None
    if not isinstance(value, dict):
        raise ValueError('Invalid window')
    used = value.get('utilization')
    if type(used) not in (int, float) or not math.isfinite(used) or not 0 <= used <= 100:
        raise ValueError('Invalid utilization')
    reset = value.get('resets_at')
    if reset is None:
        # AIBar requires a reset time per window; drop the window rather than send it half-filled.
        return None
    if not isinstance(reset, str):
        raise ValueError('Invalid reset')
    parsed = datetime.fromisoformat(reset.replace('Z', '+00:00'))
    if parsed.tzinfo is None:
        raise ValueError('Missing reset timezone')
    return dict(percentageUsed=used, resetAt=utc_stamp(parsed))


def collect(root, *, now=None, opener=None):
    now = now or datetime.now(timezone.utc)
    unavailable = document(now, 'unavailable')
    try:
        with (Path(root) / '.credentials.json').open('r', encoding='utf-8') as stream:
            fields = json.load(stream, object_pairs_hook=unique_object)
        oauth = fields.get('claudeAiOauth')
        token = oauth.get('accessToken')
        expiry = oauth.get('expiresAt')
        # Release the parsed credential document; no other credential fields are used.
        del fields, oauth
        if not isinstance(token, str) or not token.strip() or any(ord(c) < 32 or ord(c) > 126 for c in token):
            return unavailable
        if expiry is not None:
            if type(expiry) is not int:
                return unavailable
            expires_at = datetime.fromtimestamp(expiry / 1000, timezone.utc)
            if (expires_at - now).total_seconds() <= 60:
                return document(now, 'authentication-required')
    except (OSError, ValueError, TypeError, AttributeError, OverflowError):
        return unavailable

    request = Request(ENDPOINT, headers={
        'Authorization': 'Bearer ' + token,
        'anthropic-beta': 'oauth-2025-04-20',
        'anthropic-version': '2023-06-01',
    }, method='GET')
    try:
        open_request = opener or build_opener(NoRedirect()).open
        with open_request(request, timeout=10) as response:
            if response.status in (401, 403):
                return document(now, 'authentication-required')
            if response.status != 200:
                return unavailable
            # Bound response memory as well as the published file.
            body = response.read(65537)
            if len(body) > 65536:
                return unavailable
            payload = json.loads(body, object_pairs_hook=unique_object)
            primary = window(payload.get('five_hour'))
            weekly = window(payload.get('seven_day'))
            if primary is None and weekly is None:
                return unavailable
            return document(now, 'current', primary, weekly)
    except HTTPError as error:
        error.close()
        return document(now, 'authentication-required') if error.code in (401, 403) else unavailable
    except (OSError, ValueError, TypeError, AttributeError, OverflowError, http.client.HTTPException):
        return unavailable
    finally:
        request.remove_header('Authorization')
        token = None


def encode_contract(value):
    data = json.dumps(value, separators=(',', ':'), ensure_ascii=True, allow_nan=False).encode('utf-8')
    if len(data) > 4096:
        raise ValueError('Contract too large')
    return data


def read_config(path):
    config = {}
    for line in Path(path).read_text(encoding='utf-8').splitlines():
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        key, separator, value = line.partition('=')
        if not separator or key not in ('host', 'remote_dir') or key in config:
            raise ValueError('Invalid configuration')
        config[key] = value.strip()
    host, remote_dir = config.get('host', ''), config.get('remote_dir', '')
    # A narrow alphabet protects both scp's remote syntax and Windows shell parsing.
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]*', host):
        raise ValueError('Invalid host')
    if not re.fullmatch(r'[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*', remote_dir):
        raise ValueError('Invalid remote directory')
    return host, remote_dir


def push(value, config_path, *, runner=None, temp_dir=None, filename=FILENAME, encoder=encode_contract):
    host, remote_dir = read_config(config_path)
    runner = runner or subprocess.run
    destination = remote_dir + '/' + filename
    options = ['-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10']
    run_options = dict(check=True, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                       stderr=subprocess.DEVNULL, timeout=30)
    # NamedTemporaryFile is mode 0600; its lifetime includes scp and rename.
    with tempfile.NamedTemporaryFile(suffix='.json.tmp', dir=temp_dir) as local:
        local.write(encoder(value))
        local.flush()
        # File.Replace swaps atomically and keeps the previous file on failure; Move only for
        # the first publication. .NET resolves relative paths against the process directory, so
        # both paths are made absolute from the SSH user's home.
        command = ('powershell.exe -NoProfile -NonInteractive -Command "'
                   "$ErrorActionPreference = 'Stop'; "
                   "$d = Join-Path $HOME '" + destination + "'; $s = Join-Path $HOME '" + destination + ".tmp'; "
                   "if (Test-Path -LiteralPath $d) { [System.IO.File]::Replace($s, $d, [NullString]::Value) } "
                   "else { [System.IO.File]::Move($s, $d) }\"")
        try:
            runner(['scp', *options, local.name, host + ':' + destination + '.tmp'], **run_options)
            runner(['ssh', *options, host, command], **run_options)
        except (OSError, subprocess.SubprocessError):
            # Never touch the published target on failure. Clean staging if reachable.
            cleanup = ('powershell.exe -NoProfile -NonInteractive -Command "'
                       "Remove-Item -Force -LiteralPath '" + destination
                       + ".tmp' -ErrorAction SilentlyContinue\"")
            try:
                runner(['ssh', *options, host, cleanup], **run_options)
            except (OSError, subprocess.SubprocessError):
                pass
            raise


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--once', action='store_true', help='collect once (the default)')
    parser.add_argument('--dry-run', action='store_true', help='print only safe contract JSON; do not push')
    args = parser.parse_args(argv)
    root = Path(os.environ.get('CLAUDE_CONFIG_DIR', '').strip() or Path.home() / '.claude')
    value = None
    try:
        value = collect(root)
        if args.dry_run:
            print(encode_contract(value).decode('utf-8'))
            return 0
        push(value, Path.home() / '.config/aibar/claude-remote-usage.conf')
    except Exception:
        # Never let a traceback (paths, server text) reach the journal.
        stamp = value['generatedAt'] if value else utc_stamp(datetime.now(timezone.utc).replace(microsecond=0))
        print('unavailable ' + stamp, file=sys.stderr)
        return 1
    print(value['state'] + ' ' + value['generatedAt'], file=sys.stderr)
    return 0


if __name__ == '__main__':
    sys.exit(main())

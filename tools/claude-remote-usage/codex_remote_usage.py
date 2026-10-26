#!/usr/bin/env python3
"""Read-only Gentle Shell/Pi Codex quota collector; only normalized quota leaves this host."""

import argparse
from datetime import datetime, timezone
import http.client
import json
import math
import os
from pathlib import Path
import sys
from urllib.error import HTTPError
from urllib.request import Request, build_opener

import claude_remote_usage as quota
from claude_remote_usage import NoRedirect, document, encode_contract, unique_object, utc_stamp

ENDPOINT = 'https://chatgpt.com/backend-api/wham/usage'
FILENAME = 'codex-remote-usage.json'
DURATIONS = {18000: 'fiveHour', 604800: 'weekly'}


def read_auth(root):
    with (Path(root) / 'auth.json').open('r', encoding='utf-8') as stream:
        fields = json.load(stream, object_pairs_hook=unique_object)
    if not isinstance(fields, dict):
        raise ValueError('Invalid credential object')
    return fields


def agent_dir():
    for name in ('GENTLE_SHELL_HOME', 'PI_CODING_AGENT_DIR'):
        configured = os.environ.get(name)
        if configured is not None:
            return Path(configured)
    gentle = Path.home() / '.gentle-shell/agent'
    try:
        if 'openai-codex' in read_auth(gentle):
            return gentle
    except (OSError, ValueError, TypeError):
        pass
    return Path.home() / '.pi/agent'


def header_value(value):
    return isinstance(value, str) and bool(value.strip()) and all(32 <= ord(c) <= 126 for c in value)


def window(value):
    if not isinstance(value, dict):
        raise ValueError('Invalid window')
    percentage_keys = [key for key in ('used_percent', 'percentage_used') if key in value]
    reset_keys = [key for key in ('reset_at', 'reset_time') if key in value]
    if len(percentage_keys) != 1 or len(reset_keys) > 1:
        raise ValueError('Ambiguous window')
    used = value[percentage_keys[0]]
    if type(used) not in (int, float) or not math.isfinite(used) or not 0 <= used <= 100:
        raise ValueError('Invalid percentage')
    reset = value[reset_keys[0]] if reset_keys else None
    if reset is None:
        return None  # A published window must have resetAt, just like Claude.
    if type(reset) is int:
        parsed = datetime.fromtimestamp(reset, timezone.utc)
    elif isinstance(reset, str):
        parsed = datetime.fromisoformat(reset.replace('Z', '+00:00'))
        if parsed.tzinfo is None:
            raise ValueError('Missing reset timezone')
    else:
        raise ValueError('Invalid reset')
    return dict(percentageUsed=used, resetAt=utc_stamp(parsed))


def windows(payload):
    if not isinstance(payload, dict):
        raise ValueError('Invalid payload')
    names = ('primary_window', 'secondary_window', 'five_hour', 'weekly')
    root = payload
    if 'rate_limit' in payload:
        if any(name in payload for name in names):
            raise ValueError('Ambiguous window container')
        root = payload['rate_limit']
    if not isinstance(root, dict) or any(name in root for name in ('five_hour', 'weekly')):
        raise ValueError('Invalid window container')
    result = dict(fiveHour=None, weekly=None)
    seen = set()
    for name in ('primary_window', 'secondary_window'):
        value = root.get(name)
        if value is None:
            continue
        if not isinstance(value, dict):
            raise ValueError('Invalid window')
        seconds = value.get('limit_window_seconds')
        if type(seconds) is not int or seconds not in DURATIONS or seconds in seen:
            raise ValueError('Invalid or duplicate duration')
        seen.add(seconds)
        result[DURATIONS[seconds]] = window(value)
    if all(value is None for value in result.values()):
        raise ValueError('Missing windows')
    return result['fiveHour'], result['weekly']


def collect(root, *, now=None, opener=None):
    now = now or datetime.now(timezone.utc)
    unavailable = document(now, 'unavailable')
    try:
        oauth = read_auth(root).get('openai-codex')
        if not isinstance(oauth, dict) or oauth.get('type') != 'oauth':
            return unavailable
        token, account, expiry = oauth.get('access'), oauth.get('accountId'), oauth.get('expires')
        del oauth
        if not header_value(token) or not header_value(account) or type(expiry) is not int:
            return unavailable
        expires_at = datetime.fromtimestamp(expiry / 1000, timezone.utc)
        if (expires_at - now).total_seconds() <= 60:
            return document(now, 'authentication-required')
    except (OSError, ValueError, TypeError, OverflowError):
        return unavailable

    request = Request(ENDPOINT, headers={
        'Authorization': 'Bearer ' + token,
        'ChatGPT-Account-Id': account,
        'User-Agent': 'aibar-remote-usage',
    }, method='GET')
    try:
        open_request = opener or build_opener(NoRedirect()).open
        with open_request(request, timeout=10) as response:
            if response.status in (401, 403):
                return document(now, 'authentication-required')
            if response.status != 200:
                return unavailable
            body = response.read(65537)
            if len(body) > 65536:
                return unavailable
            payload = json.loads(body, object_pairs_hook=unique_object)
            primary, weekly = windows(payload)
            return document(now, 'current', primary, weekly, quota.plan_of(payload.get('plan_type')))
    except HTTPError as error:
        error.close()
        return document(now, 'authentication-required') if error.code in (401, 403) else unavailable
    except (OSError, ValueError, TypeError, AttributeError, OverflowError, http.client.HTTPException):
        return unavailable
    finally:
        request.remove_header('Authorization')
        request.remove_header('Chatgpt-account-id')
        token = account = None


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--once', action='store_true', help='collect once (the default)')
    parser.add_argument('--dry-run', action='store_true', help='print only safe contract JSON; do not push')
    args = parser.parse_args(argv)
    value = None
    try:
        value = collect(agent_dir())
        if args.dry_run:
            print(encode_contract(value).decode('utf-8'))
            return 0
        quota.push(value, Path.home() / '.config/aibar/claude-remote-usage.conf', filename=FILENAME)
    except Exception:
        # Never expose paths, credentials, response text or tracebacks to the journal.
        stamp = value['generatedAt'] if value else utc_stamp(datetime.now(timezone.utc).replace(microsecond=0))
        print('unavailable ' + stamp, file=sys.stderr)
        return 1
    print(value['state'] + ' ' + value['generatedAt'], file=sys.stderr)
    return 0


if __name__ == '__main__':
    sys.exit(main())

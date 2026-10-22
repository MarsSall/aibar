#!/usr/bin/env python3
"""Gentle Shell/Pi openai-codex assistant usage -> bounded, aggregate-only token contract."""
import argparse
from datetime import datetime, timezone, timedelta
import json
import os
from pathlib import Path
import sys
import claude_remote_tokens as tokens
import claude_remote_usage as quota

MAX_BYTES = tokens.MAX_BYTES
MAX_ROWS = tokens.MAX_ROWS
MAX_COUNT = tokens.MAX_COUNT
PROVIDER = 'openai-codex'
MARKER = '"' + PROVIDER + '"'
FILENAME = 'codex-remote-tokens.json'
encode_contract = tokens.encode_contract


def session_roots():
    configured = os.environ.get('AIBAR_CODEX_SESSION_DIRS', '').strip()
    if configured:
        return [Path(part) for part in configured.split(os.pathsep) if part.strip()]
    home = Path.home()
    return [home / '.gentle-shell/agent/sessions', home / '.pi/agent/sessions']


def collect(roots, *, now=None, local_zone=None):
    now = now or datetime.now(timezone.utc)
    local = lambda stamp: stamp.astimezone(local_zone) if local_zone else stamp.astimezone()
    today = local(now).date()
    oldest = today - timedelta(days=29)
    # A file last written before the window cannot hold an entry inside it.
    stale_before = (now - timedelta(days=31)).timestamp()
    result = dict(schemaVersion=1, generatedAt=quota.utc_stamp(now), localDay=today.isoformat(),
                  state='current', days=[])
    roots = [Path(root) for root in roots if Path(root).is_dir() and not Path(root).is_symlink()]
    if not roots:
        result['state'] = 'unavailable'
        return result
    seen = set()
    totals = {}

    def partial(_=None):
        result['state'] = 'partial'

    def add(line):
        event = json.loads(line, object_pairs_hook=quota.unique_object)
        message = event.get('message') if isinstance(event, dict) else None
        if event.get('type') != 'message' or not isinstance(message, dict) or message.get('role') != 'assistant' \
                or message.get('provider') != PROVIDER:
            return
        model = message['model']
        if not isinstance(model, str) or not tokens.MODEL.fullmatch(model):
            raise ValueError('Invalid model')
        stamp = datetime.fromisoformat(event['timestamp'].replace('Z', '+00:00'))
        if stamp.tzinfo is None:
            raise ValueError('Missing timezone')
        day = local(stamp).date()
        if day < oldest or day > today or stamp > now:
            return
        key = event['id']
        if not isinstance(key, str) or not key or len(key) > 256:
            raise ValueError('Missing dedup identity')
        usage = message['usage']
        if 'input' not in usage or 'output' not in usage:
            raise ValueError('Missing usage counts')
        values = [usage.get(name, 0) for name in ('input', 'cacheRead', 'cacheWrite', 'output')]
        if any(type(v) is not int or not 0 <= v <= MAX_COUNT for v in values):
            raise ValueError('Invalid usage')
        if key in seen:
            return
        row = (day.isoformat(), model)
        added = [a + b for a, b in zip(totals.get(row, [0, 0, 0, 0]), values)]
        if sum(added) > MAX_COUNT:
            raise ValueError('Count overflow')
        if row not in totals and len(totals) >= MAX_ROWS:
            raise ValueError('Too many rows')
        totals[row] = added
        seen.add(key)

    try:
        for root in roots:
            for directory, subdirs, files in os.walk(root, followlinks=False, onerror=partial):
                subdirs[:] = sorted(d for d in subdirs if not (Path(directory) / d).is_symlink())
                for name in sorted(files):
                    path = Path(directory) / name
                    if not name.endswith('.jsonl') or path.is_symlink():
                        continue
                    try:
                        if path.stat().st_mtime < stale_before:
                            continue
                        with path.open(encoding='utf-8') as stream:
                            while True:
                                line = stream.readline(MAX_BYTES + 1)
                                if not line:
                                    break
                                if len(line) > MAX_BYTES:
                                    # Tool output can exceed the cap; only a lost assistant reply is a gap.
                                    if '"role":"assistant"' in line or '"role": "assistant"' in line:
                                        partial()
                                    while line and not line.endswith('\n'):
                                        line = stream.readline(MAX_BYTES + 1)
                                    continue
                                # Cheap prefilter: other providers' turns and tool output are never parsed.
                                if MARKER not in line:
                                    continue
                                try:
                                    add(line)
                                except (ValueError, KeyError, TypeError, AttributeError, OverflowError):
                                    partial()
                    except (OSError, UnicodeError):
                        partial()
    except OSError:
        partial()
    result['days'] = [dict(day=day, model=model, input=v[0], cachedInput=v[1], cacheWrite=v[2], output=v[3])
                      for (day, model), v in sorted(totals.items())]
    try:
        encode_contract(result)
    except ValueError:
        result.update(state='unavailable', days=[])
    return result


def push(value, config_path, *, runner=None, temp_dir=None):
    quota.push(value, config_path, runner=runner, temp_dir=temp_dir, filename=FILENAME, encoder=encode_contract)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--once', action='store_true')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args(argv)
    now = datetime.now(timezone.utc)
    try:
        value = collect(session_roots(), now=now)
        if args.dry_run:
            print(encode_contract(value).decode('utf-8'))
            return 0
        push(value, Path.home() / '.config/aibar/claude-remote-usage.conf')
    except Exception:
        print('unavailable ' + quota.utc_stamp(now), file=sys.stderr)
        return 1
    print(value['state'] + ' ' + value['generatedAt'], file=sys.stderr)
    return 0


if __name__ == '__main__':
    sys.exit(main())

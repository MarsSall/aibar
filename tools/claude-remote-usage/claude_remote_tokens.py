#!/usr/bin/env python3
"""Local Claude assistant usage -> bounded, aggregate-only token contract."""
import argparse
from datetime import datetime, timezone, timedelta
import sys
import json
import os
from pathlib import Path
import re
import claude_remote_usage as quota

MAX_BYTES = 1048576
MAX_ROWS = 3000
MAX_COUNT = 9223372036854775807
MODEL = re.compile(r'[a-z][a-z0-9.-]{0,99}\Z')


def encode_contract(value):
    data = json.dumps(value, separators=(',', ':'), ensure_ascii=True, allow_nan=False).encode('utf-8')
    if len(data) > MAX_BYTES:
        raise ValueError('Token contract too large')
    return data


def collect(root, *, now=None, local_zone=None):
    now = now or datetime.now(timezone.utc)
    # astimezone() without an argument uses the system zone at each timestamp (including DST).
    local = lambda stamp: stamp.astimezone(local_zone) if local_zone else stamp.astimezone()
    today = local(now).date()
    result = dict(schemaVersion=1, generatedAt=quota.utc_stamp(now), localDay=today.isoformat(),
                  state='current', days=[])
    projects = Path(root) / 'projects'
    if not projects.is_dir() or projects.is_symlink():
        result['state'] = 'unavailable'
        return result
    seen = set()
    totals = {}
    def walk_error(_):
        result['state'] = 'partial'
    try:
        for directory, subdirs, files in os.walk(projects, followlinks=False, onerror=walk_error):
            subdirs[:] = sorted(d for d in subdirs if not (Path(directory) / d).is_symlink())
            for name in sorted(files):
                path = Path(directory) / name
                if not name.endswith('.jsonl') or path.is_symlink():
                    continue
                try:
                    with path.open(encoding='utf-8') as stream:
                        while True:
                            line = stream.readline(MAX_BYTES + 1)
                            if not line:
                                break
                            if len(line) > MAX_BYTES:
                                result['state'] = 'partial'
                                # Drain this oversized line without retaining its content.
                                while line and not line.endswith('\n'):
                                    line = stream.readline(MAX_BYTES + 1)
                                continue
                            try:
                                if not line.strip():
                                    continue
                                event = json.loads(line, object_pairs_hook=quota.unique_object)
                                if event.get('type') != 'assistant':
                                    continue
                                message = event['message']
                                model = message['model']
                                if model == '<synthetic>':
                                    continue
                                stamp = datetime.fromisoformat(event['timestamp'].replace('Z', '+00:00'))
                                if stamp.tzinfo is None:
                                    raise ValueError('Missing timezone')
                                day = local(stamp).date()
                                if day < today - timedelta(days=29) or day > today or stamp > now:
                                    continue
                                if not isinstance(model, str) or not MODEL.fullmatch(model):
                                    raise ValueError('Invalid model')
                                key = (message['id'], event['requestId'])
                                if not all(isinstance(k, str) and k and len(k) <= 256 for k in key):
                                    raise ValueError('Missing dedup identity')
                                usage = message['usage']
                                if 'input_tokens' not in usage or 'output_tokens' not in usage:
                                    raise ValueError('Missing usage counts')
                                values = [usage.get(k, 0) for k in ('input_tokens', 'cache_read_input_tokens',
                                          'cache_creation_input_tokens', 'output_tokens')]
                                if any(type(v) is not int or not 0 <= v <= MAX_COUNT for v in values):
                                    raise ValueError('Invalid usage')
                                if key in seen:
                                    continue
                                previous = totals.get((day.isoformat(), model), [0, 0, 0, 0])
                                added = [a + b for a, b in zip(previous, values)]
                                if sum(added) > MAX_COUNT:
                                    raise ValueError('Count overflow')
                                if (day.isoformat(), model) not in totals and len(totals) >= MAX_ROWS:
                                    raise ValueError('Too many rows')
                                totals[(day.isoformat(), model)] = added
                                seen.add(key)
                            except (ValueError, KeyError, TypeError, AttributeError, OverflowError):
                                result['state'] = 'partial'
                except (OSError, UnicodeError):
                    result['state'] = 'partial'
    except OSError:
        result['state'] = 'partial'
    result['days'] = [dict(day=day, model=model, input=v[0], cachedInput=v[1], cacheWrite=v[2], output=v[3])
                      for (day, model), v in sorted(totals.items())]
    try:
        encode_contract(result)
    except ValueError:
        result.update(state='unavailable', days=[])
    return result


def push(value, config_path, *, runner=None, temp_dir=None):
    quota.push(value, config_path, runner=runner, temp_dir=temp_dir,
               filename='claude-remote-tokens.json', encoder=encode_contract)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--once', action='store_true')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args(argv)
    now = datetime.now(timezone.utc)
    try:
        root = Path(os.environ.get('CLAUDE_CONFIG_DIR', '').strip() or Path.home() / '.claude')
        value = collect(root, now=now)
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

"""Synthetic fixtures only; temporary log trees live inside the helper test surface."""
import importlib.util
import contextlib
import io
from unittest.mock import patch
import json
from pathlib import Path
import tempfile
import unittest
import sys
from datetime import datetime, timezone, timedelta

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
NOW = datetime(2026, 10, 19, 1, tzinfo=timezone.utc)


class TokensTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('tokens', ROOT / 'claude_remote_tokens.py')
        self.h = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.h)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'projects' / 'PRIVATE_PATH').mkdir(parents=True)

    def event(self, **kw):
        value = dict(type='assistant', timestamp='2026-10-19T00:00:00Z', requestId='PRIVATE_REQUEST',
                     message=dict(id='PRIVATE_ID', model='claude-opus-5-5', content='PRIVATE_PROMPT',
                                  usage=dict(input_tokens=10, output_tokens=20,
                                             cache_creation_input_tokens=30, cache_read_input_tokens=40)))
        value.update(kw)
        return value

    def write(self, events, name='log.jsonl'):
        (self.root / 'projects' / 'PRIVATE_PATH' / name).write_text(
            '\n'.join(json.dumps(e) for e in events), encoding='utf-8')

    def test_components_dedup_and_privacy(self):
        event = self.event()
        self.write([event, event, self.event(type='user')])
        self.write([event], 'copy.jsonl')
        result = self.h.collect(self.root, now=NOW, local_zone=timezone.utc)
        self.assertEqual(result, dict(schemaVersion=1, generatedAt='2026-10-19T01:00:00Z',
                                     localDay='2026-10-19', state='current', days=[dict(
                                         day='2026-10-19', model='claude-opus-5-5', input=10,
                                         output=20, cacheWrite=30, cachedInput=40)]))
        data = self.h.encode_contract(result)
        self.assertNotIn(b'PRIVATE', data)
        self.assertLessEqual(len(data), self.h.MAX_BYTES)

    def test_last_30_local_calendar_days_inclusive_not_utc(self):
        zone = timezone(timedelta(hours=-4))
        self.write([self.event(timestamp='2026-09-19T04:00:00Z'),
                    self.event(timestamp='2026-09-19T03:59:59Z', requestId='old'),
                    self.event(timestamp='2026-10-19T00:30:00Z', requestId='today'),
                    self.event(timestamp='2026-10-20T00:00:00Z', requestId='future')])
        result = self.h.collect(self.root, now=NOW, local_zone=zone)
        self.assertEqual(result['localDay'], '2026-10-18')
        self.assertEqual([d['day'] for d in result['days']], ['2026-09-19', '2026-10-18'])
        self.assertEqual(result['state'], 'current')

    def test_synthetic_ignored_unknown_model_preserved(self):
        synthetic = self.event()
        synthetic['message']['model'] = '<synthetic>'
        unknown = self.event(requestId='other')
        unknown['message']['model'] = 'unpriced-new-model'
        self.write([synthetic, unknown])
        result = self.h.collect(self.root, now=NOW, local_zone=timezone.utc)
        self.assertEqual(len(result['days']), 1)
        self.assertEqual(result['days'][0]['model'], 'unpriced-new-model')

    def test_bad_usage_and_malformed_tail_are_partial_not_zero(self):
        for invalid in (-1, True, 1.5, '10', 9223372036854775808):
            event = self.event()
            event['message']['usage']['input_tokens'] = invalid
            self.write([event])
            result = self.h.collect(self.root, now=NOW, local_zone=timezone.utc)
            self.assertEqual(result['state'], 'partial')
            self.assertEqual(result['days'], [])
        self.write([self.event()])
        with (self.root / 'projects' / 'PRIVATE_PATH' / 'log.jsonl').open('a') as f:
            f.write('\n{"broken"')
        self.assertEqual(self.h.collect(self.root, now=NOW)['state'], 'partial')

    def test_missing_required_counts_and_directory_errors_are_partial(self):
        event = self.event()
        del event['message']['usage']['input_tokens']
        self.write([event])
        self.assertEqual(self.h.collect(self.root, now=NOW)['state'], 'partial')
        def walk_error(*args, **kwargs):
            kwargs['onerror'](PermissionError('PRIVATE_PATH'))
            return iter(())
        with patch.object(self.h.os, 'walk', side_effect=walk_error):
            self.assertEqual(self.h.collect(self.root, now=NOW)['state'], 'partial')

    def test_symlink_logs_are_not_followed(self):
        source = self.root / 'outside.jsonl'
        source.write_text(json.dumps(self.event()))
        (self.root / 'projects' / 'PRIVATE_PATH' / 'link.jsonl').symlink_to(source)
        self.assertEqual(self.h.collect(self.root, now=NOW)['days'], [])

    def test_missing_root_is_unavailable_existing_empty_root_is_current(self):
        self.assertEqual(self.h.collect(self.root / 'missing', now=NOW)['state'], 'unavailable')
        self.assertEqual(self.h.collect(self.root, now=NOW)['state'], 'current')

    def test_push_uses_separate_filename_and_leaves_quota_contract_alone(self):
        config = self.root / 'config'
        config.write_text('host=windows\nremote_dir=AppData/Local/AIBar\n')
        calls = []
        value = self.h.collect(self.root, now=NOW)
        self.h.push(value, config, runner=lambda args, **kw: calls.append(args), temp_dir=self.root)
        self.assertEqual(len(calls), 2)
        self.assertTrue(calls[0][-1].endswith('/claude-remote-tokens.json.tmp'))
        self.assertIn('claude-remote-tokens.json', calls[1][-1])
        self.assertNotIn('claude-remote-usage.json', str(calls))

    def test_cli_dry_run_is_safe_and_failure_logs_never_include_paths(self):
        out, err = io.StringIO(), io.StringIO()
        with patch.dict('os.environ', {'CLAUDE_CONFIG_DIR': str(self.root)}), contextlib.redirect_stdout(out):
            self.assertEqual(self.h.main(['--dry-run']), 0)
        self.assertEqual(json.loads(out.getvalue())['schemaVersion'], 1)
        self.assertNotIn('PRIVATE', out.getvalue())
        with patch.dict('os.environ', {'CLAUDE_CONFIG_DIR': str(self.root)}), \
                patch.object(self.h, 'push', side_effect=OSError('PRIVATE_PATH')), contextlib.redirect_stderr(err):
            self.assertEqual(self.h.main([]), 1)
        self.assertNotIn('PRIVATE', err.getvalue())
        self.assertNotIn('Traceback', err.getvalue())

    def test_size_cap_is_enforced(self):
        with self.assertRaises(ValueError):
            self.h.encode_contract({'data': 'x' * self.h.MAX_BYTES})

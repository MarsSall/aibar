"""Synthetic Gentle Shell/Pi session fixtures only; temporary trees live inside the helper test surface."""
import contextlib
import importlib.util
import io
import json
import os
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
NOW = datetime(2026, 10, 19, 1, tzinfo=timezone.utc)


class CodexTokensTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('codex_tokens', ROOT / 'codex_remote_tokens.py')
        self.h = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.h)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.sessions = self.root / 'gentle' / 'sessions'
        (self.sessions / '--PRIVATE_PATH--').mkdir(parents=True)

    def entry(self, id='PRIVATE_ID', provider='openai-codex', model='gpt-6.1-sol', timestamp='2026-10-19T00:00:00Z',
              role='assistant', **usage):
        counts = dict(input=10, output=20, cacheRead=40, cacheWrite=0, totalTokens=70,
                      cost=dict(input=0, output=0, cacheRead=0, cacheWrite=0, total=0), reasoning=5)
        counts.update(usage)
        return dict(type='message', id=id, parentId=None, timestamp=timestamp,
                    message=dict(role=role, content='PRIVATE_PROMPT', api='openai-responses', provider=provider,
                                 model=model, usage=counts, stopReason='stop', timestamp=1792416121667))

    def write(self, entries, name='session.jsonl', root=None):
        path = (root or self.sessions) / '--PRIVATE_PATH--' / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('\n'.join(e if isinstance(e, str) else json.dumps(e) for e in entries), encoding='utf-8')
        return path

    def collect(self, roots=None, zone=timezone.utc):
        return self.h.collect(roots or [self.sessions], now=NOW, local_zone=zone)

    def test_codex_assistant_counters_only_dedup_by_entry_id(self):
        entry = self.entry()
        self.write([dict(type='session', version=3, id='s', timestamp='2026-10-19T00:00:00Z', cwd='PRIVATE_PATH'),
                    entry, entry, self.entry(id='claude', provider='claude-bridge', model='claude-opus-5-5'),
                    self.entry(id='user', role='user')])
        self.write([entry], 'fork.jsonl')
        result = self.collect()
        self.assertEqual(result, dict(schemaVersion=1, generatedAt='2026-10-19T01:00:00Z', localDay='2026-10-19',
                                      state='current', days=[dict(day='2026-10-19', model='gpt-6.1-sol', input=10,
                                                                  cachedInput=40, cacheWrite=0, output=20)]))
        data = self.h.encode_contract(result)
        self.assertNotIn(b'PRIVATE', data)

    def test_models_and_days_are_summed_separately_across_roots(self):
        pi = self.root / 'pi' / 'sessions'
        self.write([self.entry(id='a'), self.entry(id='b', input=1, output=2, cacheRead=3)])
        self.write([self.entry(id='c', model='gpt-6-astra'), self.entry(id='d', timestamp='2026-10-18T12:00:00Z')], root=pi)
        result = self.collect([self.sessions, pi])
        self.assertEqual([(d['day'], d['model'], d['input'], d['cachedInput'], d['output']) for d in result['days']],
                         [('2026-10-18', 'gpt-6.1-sol', 10, 40, 20), ('2026-10-19', 'gpt-6-astra', 10, 40, 20),
                          ('2026-10-19', 'gpt-6.1-sol', 11, 43, 22)])
        self.assertEqual(result['state'], 'current')

    def test_last_30_local_days_and_future_entries_are_excluded(self):
        zone = timezone(timedelta(hours=-4))
        self.write([self.entry(id='first', timestamp='2026-09-19T04:00:00Z'),
                    self.entry(id='old', timestamp='2026-09-19T03:59:59Z'),
                    self.entry(id='today', timestamp='2026-10-19T00:30:00Z'),
                    self.entry(id='future', timestamp='2026-10-20T00:00:00Z')])
        result = self.collect(zone=zone)
        self.assertEqual(result['localDay'], '2026-10-18')
        self.assertEqual([d['day'] for d in result['days']], ['2026-09-19', '2026-10-18'])

    def test_files_untouched_for_30_days_are_skipped(self):
        path = self.write([self.entry()])
        stale = (NOW - timedelta(days=32)).timestamp()
        os.utime(path, (stale, stale))
        self.assertEqual(self.collect()['days'], [])

    def test_invalid_codex_counters_are_partial_not_zero(self):
        for invalid in (-1, True, 1.5, '10', 9223372036854775808):
            self.write([self.entry(input=invalid)])
            result = self.collect()
            self.assertEqual(result['state'], 'partial')
            self.assertEqual(result['days'], [])
        broken = self.entry(); del broken['message']['usage']['output']
        self.write([broken])
        self.assertEqual(self.collect()['state'], 'partial')
        self.write([self.entry(), '{"broken" "openai-codex"'])
        self.assertEqual(self.collect()['state'], 'partial')

    def test_other_providers_and_malformed_non_codex_lines_do_not_mark_partial(self):
        self.write([self.entry(), '{"broken"', self.entry(id='x', provider='openai', input='bad')])
        self.assertEqual(self.collect()['state'], 'current')

    def test_oversized_lines_are_partial_only_when_they_are_assistant_messages(self):
        tool = json.dumps(dict(type='message', id='t', message=dict(role='toolResult', content='x' * (self.h.MAX_BYTES + 10))))
        self.write([tool, self.entry()])
        self.assertEqual(self.collect()['state'], 'current')
        assistant = json.dumps(dict(type='message', id='big', message=dict(role='assistant', provider='openai-codex',
                                                                           content='x' * (self.h.MAX_BYTES + 10))))
        self.write([assistant, self.entry()])
        result = self.collect()
        self.assertEqual(result['state'], 'partial')
        self.assertEqual(len(result['days']), 1)

    def test_symlinks_are_not_followed(self):
        outside = self.root / 'outside.jsonl'
        outside.write_text(json.dumps(self.entry()))
        (self.sessions / '--PRIVATE_PATH--' / 'link.jsonl').symlink_to(outside)
        self.assertEqual(self.collect()['days'], [])

    def test_no_session_root_is_unavailable_and_one_existing_root_is_enough(self):
        self.assertEqual(self.collect([self.root / 'missing'])['state'], 'unavailable')
        self.assertEqual(self.collect([self.root / 'missing', self.sessions])['state'], 'current')

    def test_default_roots_follow_agent_dir_overrides(self):
        with patch.dict('os.environ', {'AIBAR_CODEX_SESSION_DIRS': f'{self.sessions}{os.pathsep}{self.root / "pi"}'}):
            self.assertEqual(self.h.session_roots(), [self.sessions, self.root / 'pi'])
        with patch.dict('os.environ', {'AIBAR_CODEX_SESSION_DIRS': ''}), patch.object(self.h.Path, 'home', return_value=Path('/h')):
            self.assertEqual(self.h.session_roots(), [Path('/h/.gentle-shell/agent/sessions'), Path('/h/.pi/agent/sessions')])

    def test_push_uses_codex_filename_only(self):
        config = self.root / 'config'
        config.write_text('host=windows\nremote_dir=AppData/Local/AIBar\n')
        calls = []
        self.h.push(self.collect(), config, runner=lambda args, **kw: calls.append(args), temp_dir=self.root)
        self.assertEqual(len(calls), 2)
        self.assertTrue(calls[0][-1].endswith('/codex-remote-tokens.json.tmp'))
        self.assertIn('codex-remote-tokens.json', calls[1][-1])
        self.assertNotIn('claude-remote-tokens.json', str(calls)); self.assertNotIn('claude-remote-usage.json', str(calls))

    def test_cli_dry_run_and_failures_never_leak_paths(self):
        self.write([self.entry()])
        out, err = io.StringIO(), io.StringIO()
        with patch.dict('os.environ', {'AIBAR_CODEX_SESSION_DIRS': str(self.sessions)}), contextlib.redirect_stdout(out):
            self.assertEqual(self.h.main(['--dry-run']), 0)
        self.assertEqual(json.loads(out.getvalue())['schemaVersion'], 1)
        self.assertNotIn('PRIVATE', out.getvalue())
        with patch.dict('os.environ', {'AIBAR_CODEX_SESSION_DIRS': str(self.sessions)}), \
                patch.object(self.h, 'push', side_effect=OSError('PRIVATE_PATH')), contextlib.redirect_stderr(err):
            self.assertEqual(self.h.main([]), 1)
        self.assertNotIn('PRIVATE', err.getvalue())
        self.assertNotIn('Traceback', err.getvalue())


if __name__ == '__main__':
    unittest.main()

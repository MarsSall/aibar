import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from unittest.mock import patch
from urllib.error import HTTPError, URLError
from urllib.request import Request

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
NOW = datetime(2026, 10, 18, 2, tzinfo=timezone.utc)
TOKEN = 'SYNTHETIC_ACCESS_CANARY'
ACCOUNT = 'SYNTHETIC_ACCOUNT_CANARY'


class CodexUsageTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue((ROOT / 'codex_remote_usage.py').is_file(), 'Missing Codex quota helper')
        sys.path.insert(0, str(ROOT))
        self.addCleanup(lambda: sys.path.remove(str(ROOT)))
        spec = importlib.util.spec_from_file_location('codex_usage', ROOT / 'codex_remote_usage.py')
        self.h = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.h)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.auth = self.root / 'auth.json'
        self.write_auth()
        self.calls = []
        # Every test, including main/error cases, has a synthetic home and clean environment.
        self.home_patch = patch.object(self.h.Path, 'home', return_value=self.root)
        self.home_patch.start()
        self.addCleanup(self.home_patch.stop)
        self.env_patch = patch.dict('os.environ', {}, clear=True)
        self.env_patch.start()
        self.addCleanup(self.env_patch.stop)

    def write_auth(self, **overrides):
        oauth = dict(type='oauth', access=TOKEN, accountId=ACCOUNT,
                     expires=int(NOW.timestamp() * 1000) + 3600000, refresh='SYNTHETIC_REFRESH')
        oauth.update(overrides)
        self.auth.write_text(json.dumps({'openai-codex': oauth}))

    def window(self, seconds=18000, **overrides):
        value = dict(limit_window_seconds=seconds, used_percent=32, reset_at=1792303200)
        value.update(overrides)
        return value

    def body(self):
        return {'rate_limit': {'primary_window': self.window(),
                               'secondary_window': self.window(604800, used_percent=9)}}

    def opener(self, body=None, status=200, error=None, raw=None):
        def open_request(request, timeout):
            self.calls.append((Request(request.full_url, headers=dict(request.header_items())), timeout))
            if error:
                raise error
            response = io.BytesIO(raw if raw is not None else json.dumps(body).encode())
            response.status = status
            return response
        return open_request

    def collect(self, body=None, **kwargs):
        return self.h.collect(self.root, now=NOW, opener=self.opener(self.body() if body is None else body, **kwargs))

    def test_contract_headers_and_read_only(self):
        before = self.auth.read_bytes()
        result = self.collect()
        self.assertEqual(list(result), ['schemaVersion', 'generatedAt', 'state', 'fiveHour', 'weekly'])
        self.assertEqual(result['schemaVersion'], 1)
        self.assertEqual(result['generatedAt'], '2026-10-18T02:00:00Z')
        self.assertEqual(result['state'], 'current')
        self.assertEqual(result['fiveHour'], {'percentageUsed': 32, 'resetAt': '2026-10-18T06:00:00Z'})
        self.assertEqual(result['weekly']['percentageUsed'], 9)
        request, timeout = self.calls[0]
        self.assertEqual(request.full_url, 'https://chatgpt.com/backend-api/wham/usage')
        self.assertEqual(request.get_method(), 'GET')
        self.assertEqual({k.lower(): v for k, v in request.header_items()},
                         {'authorization': 'Bearer ' + TOKEN, 'chatgpt-account-id': ACCOUNT,
                          'user-agent': 'aibar-remote-usage'})
        self.assertEqual(timeout, 10)
        self.assertEqual(self.auth.read_bytes(), before)
        encoded = self.h.encode_contract(result)
        for secret in (TOKEN, ACCOUNT, 'SYNTHETIC_REFRESH', str(self.root)):
            self.assertNotIn(secret.encode(), encoded)

    def test_agent_directory_precedence(self):
        gentle = self.root / '.gentle-shell/agent'
        pi = self.root / '.pi/agent'
        gentle.mkdir(parents=True)
        pi.mkdir(parents=True)
        self.assertEqual(self.h.agent_dir(), pi)
        (gentle / 'auth.json').write_text('{}')
        self.assertEqual(self.h.agent_dir(), pi)
        (gentle / 'auth.json').write_text('{"openai-codex":null}')
        self.assertEqual(self.h.agent_dir(), gentle)  # entry presence, not validity
        for malformed in ('bad', '[]', '{"openai-codex":{},"openai-codex":{}}'):
            (gentle / 'auth.json').write_text(malformed)
            self.assertEqual(self.h.agent_dir(), pi)
        with patch.dict('os.environ', {'PI_CODING_AGENT_DIR': str(pi)}):
            self.assertEqual(self.h.agent_dir(), pi)
            with patch.dict('os.environ', {'GENTLE_SHELL_HOME': str(gentle)}):
                self.assertEqual(self.h.agent_dir(), gentle)

    def test_expiry_and_invalid_credentials_never_request(self):
        for delta in (-1, 0, 60):
            self.write_auth(expires=int((NOW.timestamp() + delta) * 1000))
            value = self.collect()
            self.assertEqual(value['state'], 'authentication-required')
            self.assertIsNone(value['fiveHour'])
            self.assertIsNone(value['weekly'])
        for overrides in ({'type': 'api_key'}, {'access': ''}, {'access': 'x\n'},
                          {'accountId': ''}, {'accountId': 'x\r'}, {'expires': None},
                          {'expires': True}, {'expires': '123'}, {'expires': 10 ** 100},
                          {'accountId': None}):
            self.write_auth(**overrides)
            self.assertEqual(self.collect()['state'], 'unavailable')
        for raw in ('bad', 'null', '[]', '{}', '{"openai-codex":[]}',
                    '{"openai-codex":{},"openai-codex":{}}',
                    '{"openai-codex":{"type":"oauth","type":"oauth"}}'):
            self.auth.write_text(raw)
            self.assertEqual(self.collect()['state'], 'unavailable')
        self.assertEqual(self.h.collect(self.root / 'missing', now=NOW)['state'], 'unavailable')
        self.assertEqual(self.calls, [])
        self.write_auth(expires=int((NOW.timestamp() + 61) * 1000))
        self.assertEqual(self.collect()['state'], 'current')

    def test_swapped_windows_and_reset_forms(self):
        for reset in (1792303200, '2026-10-18T06:00:00Z', '2026-10-18T08:00:00+02:00'):
            primary = self.window(604800)
            primary.pop('used_percent')
            primary.pop('reset_at')
            primary.update(percentage_used=100, reset_time=reset)
            result = self.collect({'primary_window': primary, 'secondary_window': self.window()})
            self.assertEqual(result['weekly'], {'percentageUsed': 100, 'resetAt': '2026-10-18T06:00:00Z'})
            self.assertEqual(result['fiveHour']['percentageUsed'], 32)

    def test_missing_reset_drops_window(self):
        missing = self.window(604800)
        missing.pop('reset_at')
        for window in (missing, self.window(604800, reset_at=None)):
            result = self.collect({'primary_window': self.window(), 'secondary_window': window})
            self.assertEqual(result['state'], 'current')
            self.assertIsNone(result['weekly'])
            self.assertEqual(self.collect({'secondary_window': window})['state'], 'unavailable')

    def test_ambiguous_and_invalid_shapes(self):
        bad_windows = [[], self.window(123), self.window(True), self.window(18000.0),
                       self.window(used_percent=True), self.window(used_percent=-1),
                       self.window(used_percent=101), self.window(used_percent='2'),
                       self.window(used_percent=float('nan')), self.window(percentage_used=12),
                       self.window(reset_time=1792303200), self.window(reset_at='bad'),
                       self.window(reset_at='2026-10-18T06:00:00'), self.window(reset_at=True),
                       self.window(reset_at=1.5), self.window(reset_at=10 ** 100)]
        for window in bad_windows:
            with self.subTest(window=window):
                self.assertEqual(self.collect({'primary_window': window})['state'], 'unavailable')
        for body in ([], {}, {'rate_limit': []}, {'primary_window': None},
                     {'primary_window': self.window(), 'secondary_window': self.window()},
                     {'primary_window': self.window(reset_at=None), 'secondary_window': self.window()},
                     {'rate_limit': {'primary_window': self.window()}, 'primary_window': None},
                     {'primary_window': self.window(), 'five_hour': None}):
            with self.subTest(body=body):
                self.assertEqual(self.collect(body)['state'], 'unavailable')

    def test_duplicate_response_keys_and_caps(self):
        for raw in (b'{"primary_window":null,"primary_window":null}',
                    b'{"primary_window":{"used_percent":1,"used_percent":2}}',
                    b'not json ' + TOKEN.encode(), b'x' * 65537):
            self.assertEqual(self.collect(raw=raw)['state'], 'unavailable')
        # Exactly 64 KiB is accepted; the next byte is rejected.
        raw = json.dumps(self.body()).encode()
        self.assertEqual(self.collect(raw=raw + b' ' * (65536 - len(raw)))['state'], 'current')
        with self.assertRaises(ValueError):
            self.h.encode_contract({'oversized': 'x' * 4096})

    def test_zero_percent_and_single_window(self):
        result = self.collect({'secondary_window': self.window(604800, used_percent=0)})
        self.assertEqual(result['state'], 'current')
        self.assertIsNone(result['fiveHour'])
        self.assertEqual(result['weekly']['percentageUsed'], 0)

    def test_plan_whitelist(self):
        for plan in ('plus', 'PRO', 'free', 'team', 'business', 'enterprise', 'edu', 'max'):
            result = self.collect(dict(self.body(), plan_type=plan))
            self.assertEqual(result['plan'], plan.lower())
            self.assertEqual(list(result)[-1], 'plan')
        for plan in (None, 12, 'secret account text', 'plus\n', ''):
            self.assertNotIn('plan', self.collect(dict(self.body(), plan_type=plan)))

    def test_http_errors_and_redirect_policy(self):
        for status in (301, 302, 307, 401, 403, 429, 500):
            expected = 'authentication-required' if status in (401, 403) else 'unavailable'
            self.assertEqual(self.collect(status=status)['state'], expected)
            error = HTTPError('https://example.invalid', status, TOKEN, {}, io.BytesIO())
            self.assertEqual(self.collect(error=error)['state'], expected)
        import http.client
        for error in (URLError(TOKEN), TimeoutError(TOKEN), http.client.BadStatusLine(TOKEN)):
            self.assertEqual(self.collect(error=error)['state'], 'unavailable')
        self.assertIsNone(self.h.NoRedirect().redirect_request(None, None, 302, '', {}, 'https://example.invalid'))
        with patch.object(self.h, 'build_opener') as build:
            build.return_value.open.side_effect = URLError(TOKEN)
            self.h.collect(self.root, now=NOW)
            self.assertIsInstance(build.call_args.args[0], self.h.NoRedirect)

    def test_main_dry_run_and_shared_push(self):
        value = self.collect()
        out, err = io.StringIO(), io.StringIO()
        with patch.object(self.h, 'agent_dir', return_value=self.root), \
                patch.object(self.h, 'collect', return_value=value) as collect, \
                patch.object(self.h.quota, 'push') as push, \
                contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            self.assertEqual(self.h.main(['--once', '--dry-run']), 0)
            collect.assert_called_once_with(self.root)
            push.assert_not_called()
            self.assertEqual(json.loads(out.getvalue()), value)
            self.assertEqual(err.getvalue(), '')
            out.seek(0)
            out.truncate()
            self.assertEqual(self.h.main(['--once']), 0)
            push.assert_called_once_with(value, self.root / '.config/aibar/claude-remote-usage.conf',
                                         filename='codex-remote-usage.json')
            self.assertEqual(out.getvalue(), '')
            self.assertEqual(err.getvalue(), 'current 2026-10-18T02:00:00Z\n')

    def test_push_reuses_atomic_command(self):
        config = self.root / 'push.conf'
        config.write_text('host=test-windows\nremote_dir=AppData/Local/AIBar\n')
        calls = []
        self.h.quota.push(self.collect(), config, filename=self.h.FILENAME,
                          runner=lambda args, **kwargs: calls.append(args), temp_dir=self.root)
        self.assertEqual([args[0] for args in calls], ['scp', 'ssh'])
        self.assertEqual(calls[0][-1], 'test-windows:AppData/Local/AIBar/codex-remote-usage.json.tmp')
        self.assertIn('[System.IO.File]::Replace(', calls[1][-1])
        self.assertIn('[NullString]::Value', calls[1][-1])

    def test_sanitized_failure_logs(self):
        for target in ('collect', 'push'):
            out, err = io.StringIO(), io.StringIO()
            with patch.object(self.h, 'collect', return_value=self.h.document(NOW, 'unavailable')) as collect, \
                    patch.object(self.h.quota, 'push') as push, \
                    contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                (collect if target == 'collect' else push).side_effect = RuntimeError(TOKEN + str(self.root))
                self.assertEqual(self.h.main([]), 1)
            self.assertEqual(out.getvalue(), '')
            self.assertRegex(err.getvalue(), r'^unavailable \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\n$')
            self.assertNotIn(TOKEN, err.getvalue())
            self.assertNotIn('Traceback', err.getvalue())


if __name__ == '__main__':
    unittest.main()

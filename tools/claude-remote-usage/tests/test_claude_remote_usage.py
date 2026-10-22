import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
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
TOKEN = 'SYNTHETIC_ACCESS_PRIVACY_CANARY'
REFRESH = 'SYNTHETIC_REFRESH_PRIVACY_CANARY'


class HelperTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue((ROOT / 'claude_remote_usage.py').is_file(),
                        'Missing helper behavior: credential-to-contract collector and atomic push')
        spec = importlib.util.spec_from_file_location('helper', ROOT / 'claude_remote_usage.py')
        self.h = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.h)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.credentials = self.root / '.credentials.json'
        self.write_credentials()
        self.calls = []

    def write_credentials(self, **overrides):
        oauth = dict(accessToken=TOKEN, expiresAt=int(NOW.timestamp() * 1000) + 3600000,
                     refreshToken=REFRESH)
        oauth.update(overrides)
        self.credentials.write_text(json.dumps({'claudeAiOauth': oauth}))

    def opener(self, body=None, status=200, error=None):
        outer = self
        class Response(io.BytesIO):
            pass
        def open_request(request, timeout):
            outer.calls.append((Request(request.full_url, headers=dict(request.header_items())), timeout))
            if error:
                raise error
            response = Response(json.dumps(body).encode())
            response.status = status
            return response
        return open_request

    def collect(self, body=None, **kwargs):
        return self.h.collect(self.root, now=NOW, opener=self.opener(body, **kwargs))

    def test_contract_and_headers(self):
        result = self.collect({'five_hour': {'utilization': 32, 'resets_at': '2026-10-18T08:27:00+02:00'},
                               'seven_day': {'utilization': 9, 'resets_at': '2026-10-20T11:17:00Z'}})
        self.assertEqual(result, dict(schemaVersion=1, generatedAt='2026-10-18T02:00:00Z', state='current',
                                     fiveHour=dict(percentageUsed=32, resetAt='2026-10-18T06:27:00Z'),
                                     weekly=dict(percentageUsed=9, resetAt='2026-10-20T11:17:00Z')))
        request, timeout = self.calls[0]
        self.assertEqual(request.full_url, 'https://api.anthropic.com/api/oauth/usage')
        self.assertEqual(request.get_method(), 'GET')
        headers = {key.lower(): value for key, value in request.header_items()}
        self.assertEqual(headers, {'authorization': 'Bearer ' + TOKEN, 'anthropic-beta': 'oauth-2025-04-20',
                                   'anthropic-version': '2023-06-01'})
        self.assertEqual(timeout, 10)
        self.assertEqual(list(result), ['schemaVersion', 'generatedAt', 'state', 'fiveHour', 'weekly'])
        self.assertLessEqual(len(self.h.encode_contract(result)), 4096)

    def test_known_plan_is_sent_last_and_unknown_or_missing_is_omitted(self):
        body = {'five_hour': {'utilization': 32, 'resets_at': '2026-10-18T06:27:00Z'}, 'seven_day': None}
        self.write_credentials(subscriptionType='Max')
        result = self.collect(body)
        self.assertEqual(list(result), ['schemaVersion', 'generatedAt', 'state', 'fiveHour', 'weekly', 'plan'])
        self.assertEqual(result['plan'], 'max')
        for value in ('lifetime', 42, '', 'max\n'):
            self.write_credentials(subscriptionType=value)
            self.assertNotIn('plan', self.collect(body))
        self.write_credentials()
        self.assertNotIn('plan', self.collect(body))
        self.write_credentials(subscriptionType='pro', expiresAt=int(NOW.timestamp() * 1000))
        self.assertNotIn('plan', self.collect(body))  # non-current documents stay minimal

    def test_expiry_margin_no_http(self):
        for delta in (-1, 0, 60):
            self.write_credentials(expiresAt=int((NOW.timestamp() + delta) * 1000))
            self.assertEqual(self.collect()['state'], 'authentication-required')
        self.assertEqual(self.calls, [])

    def test_missing_expiry_matches_local_provider(self):
        self.write_credentials(expiresAt=None)
        self.assertEqual(self.collect({'five_hour': {'utilization': 0, 'resets_at': '2026-10-18T06:27:00Z'}})['state'], 'current')

    def test_invalid_credentials_no_http(self):
        for body in ('bad json', 'null', '[]', '{}', '{"claudeAiOauth":[]}',
                     '{"claudeAiOauth":{"accessToken":" "}}',
                     '{"claudeAiOauth":{"accessToken":"x","expiresAt":"bad"}}'):
            self.credentials.write_text(body)
            self.assertEqual(self.collect()['state'], 'unavailable')
        self.assertEqual(self.calls, [])

    def test_missing_credentials(self):
        self.assertEqual(self.h.collect(self.root / 'missing', now=NOW)['state'], 'unavailable')

    def test_credentials_read_only_and_refresh_unused(self):
        before = self.credentials.read_bytes()
        self.collect({'seven_day': {'utilization': 100, 'resets_at': '2026-10-18T06:27:00Z'}})
        self.assertEqual(self.credentials.read_bytes(), before)
        self.assertNotIn(REFRESH, str(self.calls))

    def test_window_null_and_missing_reset(self):
        # A window without a reset time is dropped (AIBar requires resetAt), never sent half-filled.
        valid = {'utilization': 32, 'resets_at': '2026-10-18T06:27:00+00:00'}
        for window in ({'utilization': 0}, {'utilization': 100, 'resets_at': None}):
            with self.subTest(window=window):
                result = self.collect({'five_hour': valid, 'seven_day': window})
                self.assertEqual(result['state'], 'current')
                self.assertIsNone(result['weekly'])
                self.assertEqual(result['fiveHour']['resetAt'], '2026-10-18T06:27:00Z')
                self.assertEqual(self.collect({'seven_day': window})['state'], 'unavailable')

    def test_malformed_payloads(self):
        for body in (None, [], {}, {'five_hour': None}, {'five_hour': []},
                     {'five_hour': {'utilization': True}}, {'five_hour': {'utilization': -1}},
                     {'five_hour': {'utilization': 101}}, {'five_hour': {'utilization': '12'}},
                     {'five_hour': {'utilization': 12, 'resets_at': 'bad'}},
                     {'five_hour': {'utilization': 12, 'resets_at': '2026-10-18T02:00:00'}},
                     {'five_hour': {'utilization': float('nan')}}):
            with self.subTest(body=body):
                self.assertEqual(self.collect(body)['state'], 'unavailable')

    def test_http_status_and_failure_mapping(self):
        for status in (301, 302, 401, 403, 429, 500):
            expected = 'authentication-required' if status in (401, 403) else 'unavailable'
            self.assertEqual(self.collect(status=status)['state'], expected)
            error = HTTPError('https://example.invalid', status, TOKEN, {}, io.BytesIO(TOKEN.encode()))
            self.assertEqual(self.collect(error=error)['state'], expected)
        for error in (URLError(TOKEN), TimeoutError(TOKEN), OSError(TOKEN)):
            self.assertEqual(self.collect(error=error)['state'], 'unavailable')

    def test_redirect_handler_rejects_without_following(self):
        handler = self.h.NoRedirect()
        self.assertIsNone(handler.redirect_request(None, None, 302, '', {}, 'https://example.invalid'))
        with patch.object(self.h, 'build_opener') as build:
            build.return_value.open.side_effect = URLError('fake')
            self.h.collect(self.root, now=NOW)
            self.assertIsInstance(build.call_args.args[0], self.h.NoRedirect)

    def test_duplicate_fields_rejected(self):
        def open_request(request, timeout):
            response = io.BytesIO(b'{"five_hour":{"utilization":1,"utilization":2}}')
            response.status = 200
            return response
        self.assertEqual(self.h.collect(self.root, now=NOW, opener=open_request)['state'], 'unavailable')

    def config(self, text='host=mars-windows\nremote_dir=AppData/Local/AIBar\n'):
        path = self.root / 'helper.conf'
        path.write_text(text)
        return path

    def test_push_atomic_order_and_privacy(self):
        document = self.collect({'five_hour': {'utilization': 12, 'resets_at': '2026-10-18T06:27:00Z'}})
        calls = []
        files = []
        def runner(args, **kwargs):
            calls.append((args, kwargs))
            if args[0] == 'scp':
                files.append(Path(args[-2]))
                data = files[-1].read_bytes()
                self.assertEqual(json.loads(data), document)
                self.assertNotIn(TOKEN.encode(), data)
                self.assertNotIn(REFRESH.encode(), data)
            return subprocess.CompletedProcess(args, 0)
        self.h.push(document, self.config(), runner=runner, temp_dir=self.root)
        self.assertEqual([call[0][0] for call in calls], ['scp', 'ssh'])
        self.assertEqual(calls[0][0][-1], 'mars-windows:AppData/Local/AIBar/claude-remote-usage.json.tmp')
        command = calls[1][0][-1]
        # Atomic publication: replace an existing file, move only on first publication.
        self.assertNotIn('Move-Item', command)
        self.assertIn('[System.IO.File]::Replace(', command)
        # PowerShell turns $null into "" for .NET string parameters; File.Replace rejects "".
        self.assertIn('[NullString]::Value', command)
        self.assertNotIn('$null)', command)
        self.assertIn('[System.IO.File]::Move(', command)
        self.assertIn('Test-Path', command)
        self.assertIn('-LiteralPath', command)
        self.assertIn('claude-remote-usage.json.tmp', command)
        self.assertIn('claude-remote-usage.json', command)
        for args, kwargs in calls:
            self.assertIn('BatchMode=yes', args)
            self.assertIn('ConnectTimeout=10', args)
            self.assertNotIn('shell', kwargs)
            self.assertEqual(kwargs['stdout'], subprocess.DEVNULL)
            self.assertEqual(kwargs['stderr'], subprocess.DEVNULL)
            self.assertNotIn(TOKEN, str(args))
        self.assertFalse(files[0].exists())

    def test_push_failure_does_not_rename(self):
        calls = []
        def runner(args, **kwargs):
            calls.append(args)
            raise subprocess.CalledProcessError(1, args, output=TOKEN)
        with self.assertRaises(subprocess.CalledProcessError):
            self.h.push(self.collect(), self.config(), runner=runner, temp_dir=self.root)
        self.assertEqual(len(calls), 2)
        self.assertEqual(calls[1][0], 'ssh')
        self.assertIn('Remove-Item -Force -LiteralPath', calls[1][-1])
        self.assertNotIn('Move-Item', calls[1][-1])
        self.assertEqual(list(self.root.glob('*.json.tmp')), [])

    def test_rename_failure_cleans_staging_and_preserves_error(self):
        calls = []
        def runner(args, **kwargs):
            calls.append(args)
            if len(calls) == 2:
                raise subprocess.CalledProcessError(1, args)
            return subprocess.CompletedProcess(args, 0)
        with self.assertRaises(subprocess.CalledProcessError):
            self.h.push(self.collect(), self.config(), runner=runner, temp_dir=self.root)
        self.assertEqual(len(calls), 3)
        self.assertIn('Remove-Item -Force -LiteralPath', calls[2][-1])
        self.assertIn('claude-remote-usage.json.tmp', calls[2][-1])

    def test_raw_response_and_size_fail_closed(self):
        for body in (b'not json ' + TOKEN.encode(), b'x' * 65537):
            def open_request(request, timeout):
                response = io.BytesIO(body)
                response.status = 200
                return response
            result = self.h.collect(self.root, now=NOW, opener=open_request)
            self.assertEqual(result['state'], 'unavailable')
            self.assertNotIn(TOKEN.encode(), self.h.encode_contract(result))

    def test_home_fallback_and_default_config(self):
        with patch.dict('os.environ', {}, clear=True), patch.object(self.h.Path, 'home', return_value=self.root), \
                patch.object(self.h, 'collect', return_value=self.h.document(NOW, 'unavailable')) as collect, \
                patch.object(self.h, 'push') as push, contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(self.h.main([]), 0)
            self.assertEqual(collect.call_args.args[0], self.root / '.claude')
            self.assertEqual(push.call_args.args[1], self.root / '.config/aibar/claude-remote-usage.conf')

    def test_invalid_config_never_spawns(self):
        for text in ('host=-oops\nremote_dir=AppData/AIBar', 'host=x;evil\nremote_dir=a',
                     'host=x\nremote_dir=../a', 'host=x\nremote_dir=a\'b',
                     'host=x\nremote_dir=/absolute', 'host=x', 'host=x\nhost=y\nremote_dir=a'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                self.h.push(self.collect(), self.config(text), runner=lambda *a, **k: self.fail('spawned'))

    def test_dry_run_exact_safe_json_and_config_resolution(self):
        out, err = io.StringIO(), io.StringIO()
        original_collect = self.h.collect
        def synthetic_collect(root):
            return original_collect(root, now=NOW,
                                    opener=self.opener({'five_hour': {'utilization': 12, 'resets_at': '2026-10-18T06:27:00Z'}}))
        with patch.dict('os.environ', {'CLAUDE_CONFIG_DIR': str(self.root)}, clear=True), \
                patch.object(self.h, 'collect', wraps=synthetic_collect) as collect, \
                patch.object(self.h, 'push') as push, contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            self.assertEqual(self.h.main(['--once', '--dry-run']), 0)
            self.assertEqual(collect.call_args.args[0], self.root)
            push.assert_not_called()
        self.assertEqual(json.loads(out.getvalue())['state'], 'current')
        self.assertEqual(err.getvalue(), '')
        self.assertNotIn(TOKEN, out.getvalue())
        self.assertNotIn(REFRESH, out.getvalue())
        self.assertNotIn(str(self.root), out.getvalue())

    def test_protocol_errors_are_unavailable_without_leaking(self):
        import http.client
        for error in (http.client.BadStatusLine(TOKEN), http.client.IncompleteRead(TOKEN.encode()),
                      http.client.RemoteDisconnected(TOKEN)):
            with self.subTest(error=type(error).__name__):
                result = self.collect(error=error)
                self.assertEqual(result['state'], 'unavailable')
                self.assertNotIn(TOKEN, json.dumps(result))

    def test_unexpected_errors_never_reach_stderr_as_tracebacks(self):
        for patched in ('collect', 'push'):
            with self.subTest(patched=patched):
                out, err = io.StringIO(), io.StringIO()
                document = self.h.document(NOW, 'unavailable')
                side = RuntimeError(TOKEN + str(self.root))
                with patch.object(self.h, 'collect', side_effect=side) if patched == 'collect' else \
                        patch.object(self.h, 'collect', return_value=document), \
                        patch.object(self.h, 'push', side_effect=side), \
                        contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                    self.assertEqual(self.h.main([]), 1)
                self.assertNotIn(TOKEN, err.getvalue())
                self.assertNotIn('Traceback', err.getvalue())
                self.assertRegex(err.getvalue(), r'^unavailable \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\n$')

    def test_default_once_and_failure_logs_are_sanitized(self):
        out, err = io.StringIO(), io.StringIO()
        document = self.h.document(NOW, 'unavailable')
        with patch.object(self.h, 'collect', return_value=document), \
                patch.object(self.h, 'push', side_effect=OSError(TOKEN + str(self.root))), \
                contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            self.assertEqual(self.h.main([]), 1)
        self.assertEqual(out.getvalue(), '')
        self.assertEqual(err.getvalue(), 'unavailable 2026-10-18T02:00:00Z\n')


if __name__ == '__main__':
    unittest.main()

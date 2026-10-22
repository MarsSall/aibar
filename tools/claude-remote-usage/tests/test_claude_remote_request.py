import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
NOW = datetime(2026, 10, 22, 9, 48, 30, tzinfo=timezone.utc)
REQUEST = '{"schemaVersion":1,"requestedAt":"2026-10-22T09:48:16Z"}'


class RequestWatcherTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue((ROOT / 'claude_remote_request.py').is_file(),
                        'Missing watcher behavior: Windows refresh requests start the collector')
        sys.path.insert(0, str(ROOT))
        self.addCleanup(sys.path.remove, str(ROOT))
        spec = importlib.util.spec_from_file_location('watcher', ROOT / 'claude_remote_request.py')
        self.w = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.w)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT)
        self.addCleanup(self.temp.cleanup)
        self.config = Path(self.temp.name) / 'claude-remote-usage.conf'
        self.config.write_text('host=mars-windows\nremote_dir=AppData/Local/AIBar\n', encoding='utf-8')
        self.calls = []

    def runner(self, request=REQUEST, ssh_error=None, start_error=None):
        def run(args, **kwargs):
            self.calls.append((args, kwargs))
            if args[0] == 'ssh':
                if ssh_error:
                    raise ssh_error
                return subprocess.CompletedProcess(args, 0, stdout=request.encode('utf-8'))
            if start_error:
                raise start_error
            return subprocess.CompletedProcess(args, 0)
        return run

    def check(self, state, now=NOW, **runner):
        return self.w.check(self.config, state, now, runner=self.runner(**runner), control_dir=self.temp.name)

    def starts(self):
        return [args for args, _ in self.calls if args[0] == 'systemctl']

    def test_new_request_starts_the_existing_collector_service_once(self):
        state = self.w.State()
        self.assertEqual('requested', self.check(state))
        self.assertEqual([['systemctl', '--user', 'start', 'aibar-claude-remote-usage.service']], self.starts())
        self.assertEqual('none', self.check(state, NOW + timedelta(seconds=15)))
        self.assertEqual(1, len(self.starts()))

    def test_ssh_reads_only_the_request_file_over_a_shared_master(self):
        self.check(self.w.State())
        args, kwargs = self.calls[0]
        self.assertEqual('ssh', args[0])
        for option in ('BatchMode=yes', 'ConnectTimeout=10', 'ControlMaster=auto', 'ControlPersist=120'):
            self.assertIn(option, args)
        self.assertTrue(any(value.startswith('ControlPath=' + self.temp.name) for value in args))
        self.assertIn('mars-windows', args)
        self.assertIn("AppData/Local/AIBar/claude-refresh-request.json", args[-1])
        self.assertNotIn('claude-remote-usage.json', args[-1])
        self.assertEqual(30, kwargs['timeout'])
        self.assertIs(subprocess.DEVNULL, kwargs['stdin'])

    def test_requests_are_throttled_to_one_query_per_minute_then_served(self):
        state = self.w.State()
        self.check(state)
        second = REQUEST.replace('09:48:16', '09:48:40')
        self.assertEqual('throttled', self.check(state, NOW + timedelta(seconds=20), request=second))
        self.assertEqual(1, len(self.starts()))
        self.assertEqual('requested', self.check(state, NOW + timedelta(seconds=60), request=second))
        self.assertEqual(2, len(self.starts()))

    def test_missing_invalid_or_old_requests_never_query(self):
        for request in ('', 'not json', '{"schemaVersion":2,"requestedAt":"2026-10-22T09:48:16Z"}',
                        '{"schemaVersion":1,"requestedAt":"2026-10-22T09:48:16"}',
                        '{"schemaVersion":1,"requestedAt":"2026-10-22T09:48:16Z","extra":1}',
                        '{"schemaVersion":1,"requestedAt":"2026-10-22T09:30:00Z"}',
                        '{"schemaVersion":1,"requestedAt":"2026-10-22T10:30:00Z"}', 'x' * 5000):
            with self.subTest(request=request[:40]):
                self.assertEqual('none', self.check(self.w.State(), request=request))
        self.assertEqual([], self.starts())

    def test_unreachable_windows_or_failed_start_is_safe_and_retried(self):
        state = self.w.State()
        self.assertEqual('unreachable', self.check(state, ssh_error=subprocess.TimeoutExpired('ssh', 30)))
        self.assertEqual('failed', self.check(state, start_error=subprocess.CalledProcessError(1, 'systemctl')))
        self.assertEqual('requested', self.check(state, NOW + timedelta(seconds=60)))

    def test_watch_checks_every_fifteen_seconds(self):
        sleeps = []
        clock = iter(NOW + timedelta(seconds=15 * index) for index in range(3))
        self.w.watch(self.config, runner=self.runner(), control_dir=self.temp.name, now=lambda: next(clock),
                     sleep=sleeps.append, iterations=3)
        self.assertEqual([15, 15, 15], sleeps)
        self.assertEqual(1, len(self.starts()))

    def test_cli_rejects_unknown_options(self):
        with self.assertRaises(SystemExit):
            self.w.main(['--loop'])


if __name__ == '__main__':
    unittest.main()

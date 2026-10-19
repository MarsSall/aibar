using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaAlertTrayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-alert-tray-{Guid.NewGuid():N}");
    [Fact]
    public void Toggles_persist_checkmarks_balloon_click_shows_panel_and_limit_glows_once()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = new QuotaAlertObserver(_root, new FixedClock(now));
        try
        {
            var tray = new Tray(); var opened = 0; var glows = 0;
            using var controller = new QuotaAlertTrayController(tray, owner, Dispatcher.CurrentDispatcher, () => opened++, _ => glows++);
            PumpUntil(() => tray.Options is not null);
            tray.Toggle("resets"); PumpUntil(() => tray.Options?.Resets == false);
            Assert.False(owner.Settings.LoadAsync(default).AsTask().GetAwaiter().GetResult().Resets);
            tray.Click(); Assert.Equal(1, opened);
            var update = new QuotaAuthorityUpdate(new(new(new(100, now.AddHours(2)), null, now), FreshnessState.Current, false, null, null), 1, 1);
            owner.Observe("Claude", update, () => true);
            PumpUntil(() => tray.Messages.Count == 4 && glows == 1);
            Assert.Contains(tray.Messages, m => m.Title == "Claude 5-hour limit at 100%" && m.Body.StartsWith("Resets in "));
            owner.Observe("Claude", update, () => true);
            owner.Completion.GetAwaiter().GetResult();
            Assert.Equal(4, tray.Messages.Count); Assert.Equal(1, glows);
            controller.Dispose(); tray.Click(); Assert.Equal(1, opened);
        }
        finally { owner.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    [Fact]
    public void Edge_glow_toggle_is_independent_of_threshold_balloons()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = new QuotaAlertObserver(_root, new FixedClock(now));
        try
        {
            owner.Settings.SaveAsync(new(false, false, false, true), default).AsTask().GetAwaiter().GetResult();
            var tray = new Tray(); var glows = 0;
            using var controller = new QuotaAlertTrayController(tray, owner, Dispatcher.CurrentDispatcher, () => { }, _ => glows++);
            owner.Observe("Codex", new(new(new(new(100, now.AddHours(2)), null, now), FreshnessState.Current, false, null, null), 1, 1), () => true);
            PumpUntil(() => glows == 1);
            Assert.Empty(tray.Messages);
        }
        finally { owner.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var deadline = DateTime.UtcNow.AddSeconds(5);
        timer.Tick += (_, _) => frame.Continue = !condition() && DateTime.UtcNow < deadline;
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Assert.True(condition());
    }
    private sealed class Tray : IQuotaAlertTray
    {
        public event Action<string>? AlertToggleRequested;
        public event Action? AlertClicked;
        public AlertOptions? Options;
        public readonly List<(string Title, string Body)> Messages = [];
        public void SetAlertOptions(AlertOptions options) => Options = options;
        public void ShowAlert(string title, string body) => Messages.Add((title, body));
        public void Toggle(string key) => AlertToggleRequested?.Invoke(key);
        public void Click() => AlertClicked?.Invoke();
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

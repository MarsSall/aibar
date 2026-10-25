using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaAlertTrayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-alert-tray-{Guid.NewGuid():N}");
    [Fact]
    public void Settings_changes_persist_balloon_click_shows_panel_and_limit_glows_once()
    {
        var now = DateTimeOffset.UtcNow;
        var owner = new QuotaAlertObserver(_root, new FixedClock(now));
        try
        {
            var tray = new Tray(); var opened = 0; var glows = 0;
            using var controller = new QuotaAlertTrayController(tray, owner, Dispatcher.CurrentDispatcher, () => opened++, _ => glows++);
            owner.ChangeSettingsAsync(options => options with { Resets = false }).GetAwaiter().GetResult();
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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Custom_card_is_primary_and_balloon_is_only_a_failure_fallback(bool canShow)
    {
        var now = DateTimeOffset.UtcNow;
        var owner = new QuotaAlertObserver(_root, new FixedClock(now));
        var cards = new Cards(canShow);
        try
        {
            var tray = new Tray();
            using (var controller = new QuotaAlertTrayController(tray, owner, Dispatcher.CurrentDispatcher,
                () => { }, _ => { }, _ => { }, cards))
            {
                owner.Observe("Claude", new(new(new(new(76, now.AddHours(2)), null, now), FreshnessState.Current, false, null, null), 1, 1), () => true);
                PumpUntil(() => cards.Messages.Count != 0);
                Assert.Contains(cards.Messages, m => m.Kind == QuotaAlertKind.ThresholdCrossed && m.Provider == "Claude");
                Assert.Equal(canShow ? 0 : cards.Messages.Count, tray.Messages.Count);
            }
            Assert.True(cards.Disposed);
        }
        finally { owner.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private sealed class Cards(bool canShow) : INotificationDelivery
    {
        public bool Disposed;
        public List<(QuotaAlertKind Kind, string Provider)> Messages = [];
        public bool TryShow(QuotaAlertKind kind, string provider, string title, string body)
        {
            Messages.Add((kind, provider)); return canShow;
        }
        public void Dispose() => Disposed = true;
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        timer.Tick += (_, _) => frame.Continue = !condition() && DateTime.UtcNow < deadline;
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Assert.True(condition());
    }
    private sealed class Tray : IQuotaAlertTray
    {
        public event Action? AlertClicked;
        public readonly List<(string Title, string Body)> Messages = [];
        public void ShowAlert(string title, string body) => Messages.Add((title, body));
        public void Click() => AlertClicked?.Invoke();
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

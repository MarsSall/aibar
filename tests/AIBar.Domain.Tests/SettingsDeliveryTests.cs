using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class SettingsDeliveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-settings-delivery-{Guid.NewGuid():N}");
    [Fact]
    public void Reset_confetti_is_independent_of_notifications_but_respects_reset_toggle()
    {
        var now = DateTimeOffset.UtcNow; var observer = new QuotaAlertObserver(_root, new FixedClock(now));
        try
        {
            observer.Settings.SaveAsync(new() { Notification = false, Confetti = true }, default).AsTask().GetAwaiter().GetResult();
            var tray = new Tray(); var confetti = 0;
            using var controller = new QuotaAlertTrayController(tray, observer, Dispatcher.CurrentDispatcher, () => { }, _ => { }, _ => confetti++);
            Observe(1, now, now.AddHours(1)); observer.Completion.GetAwaiter().GetResult();
            Observe(2, now.AddSeconds(1), now.AddHours(6)); observer.Completion.GetAwaiter().GetResult();
            PumpUntil(() => confetti == 1); Assert.Equal(1, confetti); Assert.Equal(0, tray.Messages);
            observer.ChangeSettingsAsync(o => o with { Resets = false }).GetAwaiter().GetResult();
            Observe(3, now.AddSeconds(2), now.AddHours(11)); observer.Completion.GetAwaiter().GetResult();
            Pump(); Assert.Equal(1, confetti); Assert.Equal(0, tray.Messages);
            void Observe(long generation, DateTimeOffset retrieved, DateTimeOffset reset) => observer.Observe("Codex",
                new(new(new(new(1, reset), null, retrieved), FreshnessState.Current, false, null, null), generation, generation), () => true);
        }
        finally { observer.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        timer.Tick += (_, _) => frame.Continue = !condition() && DateTime.UtcNow < deadline;
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Assert.True(condition());
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now.AddMinutes(1); }
    private sealed class Tray : IQuotaAlertTray
    {
        public event Action<string>? AlertToggleRequested { add { } remove { } }
        public event Action? AlertClicked { add { } remove { } }
        public int Messages;
        public void SetAlertOptions(AlertOptions options) { }
        public void ShowAlert(string title, string body) => Messages++;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

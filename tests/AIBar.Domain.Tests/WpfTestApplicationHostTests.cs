using System.Windows.Threading;

namespace AIBar.Domain.Tests;

public sealed class WpfTestApplicationHostTests
{
    // xUnit runs test classes in parallel on one shared WPF dispatcher. A test that pumps a nested
    // frame (waiting for an async save) must not let another class's callback run inside it: that
    // interleaving swapped themes and windows under SettingsWindowTests.Save_persists in CI.
    [Fact]
    public void Callbacks_from_other_threads_never_run_inside_a_pumping_callback()
    {
        using var firstInside = new ManualResetEventSlim();
        var events = new List<string>();
        var second = new Thread(() =>
        {
            firstInside.Wait();
            WpfTestApplicationHost.Run(_ => { lock (events) events.Add("second"); });
        });
        var first = new Thread(() => WpfTestApplicationHost.Run(_ =>
        {
            lock (events) events.Add("first-start");
            firstInside.Set();
            // Pump like the Settings save tests do, long enough for the second Invoke to arrive.
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
            lock (events) events.Add("first-end");
        }));
        first.Start(); second.Start();
        Assert.True(first.Join(TimeSpan.FromSeconds(30)) && second.Join(TimeSpan.FromSeconds(30)));
        Assert.Equal(new[] { "first-start", "first-end", "second" }, events);
    }
}

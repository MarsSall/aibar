using System.Diagnostics;
using System.Text.Json;
using System.Threading;
if (args[0] == "child")
{
    File.WriteAllText(args[5], JsonSerializer.Serialize(new { child = new { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks } }));
    var start = new ProcessStartInfo(args[1]) { UseShellExecute = false };
    start.ArgumentList.Add("grandchild");
    start.ArgumentList.Add(args[2]); start.ArgumentList.Add(args[3]); start.ArgumentList.Add(args[4]); start.ArgumentList.Add(args[5]); start.ArgumentList.Add(args[6]); start.ArgumentList.Add(args[7]); start.ArgumentList.Add(args[8]);
    using var grandchild = Process.Start(start)!;
    using var childReady = EventWaitHandle.OpenExisting(args[2]);
    Console.Out.WriteLine("child-stdout"); Console.Error.WriteLine("child-stderr");
    Environment.Exit(childReady.WaitOne(10000) ? int.Parse(args[9]) : 3);
}
if (args[0] == "child-only")
{
    File.WriteAllText(args[5], JsonSerializer.Serialize(new { child = new { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks } }));
    using var childOnlyReady = EventWaitHandle.OpenExisting(args[2]);
    using var childOnlyRelease = EventWaitHandle.OpenExisting(args[3]);
    childOnlyReady.Set();
    Environment.Exit(childOnlyRelease.WaitOne(10000) ? 0 : 3);
}
if (args[0] == "child-unpublished")
{
    File.WriteAllText(args[5], JsonSerializer.Serialize(new { child = new { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks } }));
    var start = new ProcessStartInfo(args[1]) { UseShellExecute = false };
    start.ArgumentList.Add("unpublished-grandchild");
    start.ArgumentList.Add(args[2]); start.ArgumentList.Add(args[3]); start.ArgumentList.Add(args[4]); start.ArgumentList.Add(args[5]); start.ArgumentList.Add(args[6]); start.ArgumentList.Add(args[7]); start.ArgumentList.Add(args[8]);
    using var grandchild = Process.Start(start)!;
    using var childUnpublishedRelease = EventWaitHandle.OpenExisting(args[3]);
    Environment.Exit(childUnpublishedRelease.WaitOne(10000) ? 0 : 3);
}
if (args[0] == "publish")
{
    var mode = Environment.GetEnvironmentVariable("AIBAR_PUBLISH_MODE");
    if (mode == "saturated") { File.WriteAllText(Environment.GetEnvironmentVariable("AIBAR_MARKER")!, $"owned-marker:{Environment.GetEnvironmentVariable("AIBAR_NONCE")}"); Console.Out.Write(new string('o', 1_048_576)); Console.Error.Write(new string('e', 1_048_576)); Environment.Exit(0); }
    if (mode == "diagnostic-nonzero") { Console.Out.Write(new string('o', 4096)); Console.Out.WriteLine("stdout-tail password=stdout-password"); Console.Error.Write(new string('e', 4096)); Console.Error.WriteLine("stderr-tail https://user:stderr-password@example.invalid/path"); Environment.Exit(23); }
    if (mode == "diagnostic-timeout") { Console.Out.WriteLine("timeout-stdout"); Console.Out.Flush(); Console.Error.WriteLine("timeout-stderr"); Console.Error.Flush(); Thread.Sleep(30000); Environment.Exit(0); }
    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("AIBAR_HARNESS_EXE")!) { UseShellExecute = false };
    foreach (var value in new[] { "publisher-child", Environment.GetEnvironmentVariable("AIBAR_HARNESS_EXE")!, Environment.GetEnvironmentVariable("AIBAR_READY")!, Environment.GetEnvironmentVariable("AIBAR_RELEASE")!, Environment.GetEnvironmentVariable("AIBAR_MARKER")!, Environment.GetEnvironmentVariable("AIBAR_IDENTITY")!, Environment.GetEnvironmentVariable("AIBAR_ARGUMENTS")!, Environment.GetEnvironmentVariable("AIBAR_ROOT")!, Environment.GetEnvironmentVariable("AIBAR_NONCE")! }) start.ArgumentList.Add(value);
    using var child = Process.Start(start)!;
    if (Environment.GetEnvironmentVariable("AIBAR_PUBLISH_MODE") == "early")
    {
        using var publisherReady = EventWaitHandle.OpenExisting(Environment.GetEnvironmentVariable("AIBAR_READY")!);
        Environment.Exit(publisherReady.WaitOne(10000) ? 0 : 5);
    }
    child.WaitForExit();
    Environment.Exit(child.ExitCode);
}
if (args[0] == "publisher-child")
{
    File.WriteAllText(args[5], JsonSerializer.Serialize(new { child = new { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks } }));
    var start = new ProcessStartInfo(args[1]) { UseShellExecute = false };
    start.ArgumentList.Add("grandchild");
    start.ArgumentList.Add(args[2]); start.ArgumentList.Add(args[3]); start.ArgumentList.Add(args[4]); start.ArgumentList.Add(args[5]); start.ArgumentList.Add(args[6]); start.ArgumentList.Add(args[7]); start.ArgumentList.Add(args[8]);
    using var grandchild = Process.Start(start)!;
    using var publisherRelease = EventWaitHandle.OpenExisting(args[3]);
    Environment.Exit(publisherRelease.WaitOne(60000) ? 0 : 4);
}
if (args[0] == "unpublished-grandchild")
{
    using var unpublishedReady = EventWaitHandle.OpenExisting(args[1]);
    using var unpublishedRelease = new EventWaitHandle(false, EventResetMode.ManualReset, $"Local\\aibar-unpublished-release-{args[7]}");
    File.WriteAllText(args[5], Environment.ProcessId.ToString());
    unpublishedReady.Set();
    Environment.Exit(unpublishedRelease.WaitOne(10000) ? 0 : 4);
}
using var ready = EventWaitHandle.OpenExisting(args[1]);
using var release = EventWaitHandle.OpenExisting(args[2]);
var identity = JsonDocument.Parse(File.ReadAllText(args[4]));
File.WriteAllText(args[4], JsonSerializer.Serialize(new { child = identity.RootElement.GetProperty("child"), grandchild = new { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks } }));
File.WriteAllText(args[5], args[6]);
File.WriteAllText(args[3], $"owned-marker:{args[7]}");
Console.Out.WriteLine("grandchild-stdout"); Console.Error.WriteLine("grandchild-stderr");
ready.Set();
Environment.Exit(release.WaitOne(10000) ? 0 : 4);

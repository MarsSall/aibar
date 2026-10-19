using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Consent-gated five-minute cadence; failures replace health with no badge.</summary>
public sealed class ProviderStatusPoller : IDisposable
{
    public static readonly TimeSpan Cadence = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly Func<StatusProvider, CancellationToken, Task<ProviderStatus?>> _fetch;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<StatusProvider, Entry> _entries = new()
    {
        [StatusProvider.Codex] = new(), [StatusProvider.Claude] = new()
    };
    private bool _disposed;
    public ProviderStatusPoller(Func<StatusProvider, CancellationToken, Task<ProviderStatus?>> fetch, Func<DateTimeOffset>? now = null)
    { _fetch = fetch; _now = now ?? (() => DateTimeOffset.UtcNow); }
    public event Action<StatusProvider, ProviderStatus?>? Changed;
    public TimeSpan NextPollDelay
    {
        get
        {
            lock (_gate)
            {
                var next = _entries.Values.Where(e => e.Enabled && !e.InFlight).Select(e => e.Next).DefaultIfEmpty(_now() + Cadence).Min();
                var delay = next - _now();
                return delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
            }
        }
    }

    public void SetEnabled(StatusProvider provider, bool enabled)
    {
        lock (_gate)
        {
            var entry = _entries.TryGetValue(provider, out var value) ? value : throw new ArgumentOutOfRangeException(nameof(provider));
            if (_disposed || entry.Enabled == enabled) return;
            entry.Enabled = enabled;
            entry.Generation++;
            entry.Cancellation?.Cancel();
            entry.Next = DateTimeOffset.MinValue;
            if (!enabled) Changed?.Invoke(provider, null);
        }
    }

    public Task RefreshAsync() => Task.WhenAll(Enum.GetValues<StatusProvider>().Select(RefreshOneAsync));
    private async Task RefreshOneAsync(StatusProvider provider)
    {
        Entry entry;
        long generation;
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            entry = _entries[provider];
            if (_disposed || !entry.Enabled || entry.InFlight || _now() < entry.Next) return;
            entry.InFlight = true;
            entry.Next = _now() + Cadence;
            generation = entry.Generation;
            cancellation = new();
            entry.Cancellation = cancellation;
        }
        ProviderStatus? result = null;
        try { result = await _fetch(provider, cancellation.Token).ConfigureAwait(false); }
        catch (HttpRequestException) { }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_gate)
            {
                entry.InFlight = false;
                entry.Cancellation = null;
                cancellation.Dispose();
            }
        }
        lock (_gate)
            if (!_disposed && entry.Enabled && generation == entry.Generation)
                Changed?.Invoke(provider, result);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var pair in _entries)
            {
                pair.Value.Cancellation?.Cancel();
                Changed?.Invoke(pair.Key, null);
            }
        }
    }
    private sealed class Entry
    {
        public bool Enabled, InFlight;
        public long Generation;
        public DateTimeOffset Next;
        public CancellationTokenSource? Cancellation;
    }
}

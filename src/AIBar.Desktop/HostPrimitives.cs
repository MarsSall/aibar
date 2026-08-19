namespace AIBar.Desktop;

public enum StartupIntent { Ordinary, Show }

public static class StartupIntentParser
{
    public static StartupIntent Parse(string[] arguments) =>
        arguments.Length == 1 && arguments[0] == "--show" ? StartupIntent.Show : StartupIntent.Ordinary;
}

public sealed class SingleInstanceHost : IDisposable
{
    private static readonly HashSet<string> ProcessOwnedNames = [];
    private readonly Mutex _ownership;
    private readonly EventWaitHandle _activation;
    private readonly string _name;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceHost(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        _ownership = new Mutex(false, $"{name}.mutex");
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, $"{name}.activate", out _);
        lock (ProcessOwnedNames)
        {
            if (!ProcessOwnedNames.Contains(name))
            {
                try { _ownsMutex = _ownership.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsMutex = true; }
                if (_ownsMutex) ProcessOwnedNames.Add(name);
            }
        }
        IsPrimary = _ownsMutex;
    }

    public bool IsPrimary { get; }
    public event Action? ActivationRequested;

    public bool RequestActivation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return !IsPrimary && _activation.Set();
    }

    public void DispatchPendingActivation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsPrimary && _activation.WaitOne(0)) ActivationRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activation.Dispose();
        if (_ownsMutex)
        {
            _ownership.ReleaseMutex();
            lock (ProcessOwnedNames) ProcessOwnedNames.Remove(_name);
            _ownsMutex = false;
        }
        _ownership.Dispose();
    }
}

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public readonly record struct PopoverPlacementInput(
    ScreenRect WorkArea,
    ScreenRect MonitorBounds,
    double LogicalWidth,
    double LogicalHeight,
    double DpiScale);

public readonly record struct PopoverBounds(double Left, double Top, double Width, double Height);

public static class PopoverPlacement
{
    public static PopoverBounds Place(PopoverPlacementInput input)
    {
        var values = new[] { input.WorkArea.Left, input.WorkArea.Top, input.WorkArea.Width, input.WorkArea.Height,
            input.MonitorBounds.Left, input.MonitorBounds.Top, input.MonitorBounds.Width, input.MonitorBounds.Height,
            input.LogicalWidth, input.LogicalHeight, input.DpiScale };
        if (values.Any(value => !double.IsFinite(value)) || input.DpiScale <= 0 ||
            input.LogicalWidth <= 0 || input.LogicalHeight <= 0 ||
            input.WorkArea.Width <= 0 || input.WorkArea.Height <= 0 ||
            input.MonitorBounds.Width <= 0 || input.MonitorBounds.Height <= 0 ||
            input.WorkArea.Left < input.MonitorBounds.Left || input.WorkArea.Top < input.MonitorBounds.Top ||
            input.WorkArea.Right > input.MonitorBounds.Right || input.WorkArea.Bottom > input.MonitorBounds.Bottom)
            throw new ArgumentOutOfRangeException(nameof(input));

        var scaledWidth = input.LogicalWidth * input.DpiScale;
        var scaledHeight = input.LogicalHeight * input.DpiScale;
        if (!double.IsFinite(scaledWidth) || !double.IsFinite(scaledHeight))
            throw new ArgumentOutOfRangeException(nameof(input));
        var width = Math.Min(scaledWidth, input.WorkArea.Width);
        var height = Math.Min(scaledHeight, input.WorkArea.Height);
        var result = new PopoverBounds((input.WorkArea.Right - width) / input.DpiScale,
            (input.WorkArea.Bottom - height) / input.DpiScale, width / input.DpiScale, height / input.DpiScale);
        if (!double.IsFinite(result.Left) || !double.IsFinite(result.Top) ||
            !double.IsFinite(result.Width) || !double.IsFinite(result.Height) ||
            result.Width <= 0 || result.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        return result;
    }
}

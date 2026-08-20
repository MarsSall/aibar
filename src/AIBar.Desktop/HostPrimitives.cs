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
    public bool RequestActivation() { ObjectDisposedException.ThrowIf(_disposed, this); return !IsPrimary && _activation.Set(); }
    public void DispatchPendingActivation() { ObjectDisposedException.ThrowIf(_disposed, this); if (IsPrimary && _activation.WaitOne(0)) ActivationRequested?.Invoke(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _activation.Dispose();
        if (_ownsMutex) { _ownership.ReleaseMutex(); lock (ProcessOwnedNames) ProcessOwnedNames.Remove(_name); _ownsMutex = false; }
        _ownership.Dispose();
    }
}

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool IsUsable => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height) && double.IsFinite(Right) && double.IsFinite(Bottom) && Width > 0 && Height > 0;
}

public readonly record struct PopoverPlacementInput(ScreenRect WorkArea, ScreenRect MonitorBounds, double LogicalWidth, double LogicalHeight, double DpiScale);
public enum TaskbarEdge { Bottom, Top, Left, Right }
public readonly record struct PopoverPlacementContext(ScreenRect WorkArea, ScreenRect MonitorBounds, ScreenRect? Cursor, ScreenRect PrimaryWorkArea, double LogicalWidth, double LogicalHeight, double DpiScale, TaskbarEdge TaskbarEdge);
public readonly record struct PopoverBounds(double Left, double Top, double Width, double Height);

public static class PopoverPlacement
{
    public static PopoverBounds Place(PopoverPlacementInput input)
    {
        if (!input.WorkArea.IsUsable || !input.MonitorBounds.IsUsable || !IsPositive(input.LogicalWidth, input.LogicalHeight, input.DpiScale) || !Contains(input.MonitorBounds, input.WorkArea))
            throw new ArgumentOutOfRangeException(nameof(input));
        var scaledWidth = input.LogicalWidth * input.DpiScale;
        var scaledHeight = input.LogicalHeight * input.DpiScale;
        if (!IsPositive(scaledWidth, scaledHeight)) throw new ArgumentOutOfRangeException(nameof(input));
        var width = Math.Min(scaledWidth, input.WorkArea.Width);
        var height = Math.Min(scaledHeight, input.WorkArea.Height);
        var result = new PopoverBounds((input.WorkArea.Right - width) / input.DpiScale,
            (input.WorkArea.Bottom - height) / input.DpiScale, width / input.DpiScale, height / input.DpiScale);
        if (!double.IsFinite(result.Left) || !double.IsFinite(result.Top) || !IsPositive(result.Width, result.Height))
            throw new ArgumentOutOfRangeException(nameof(input));
        return result;
    }

    public static PopoverBounds PlaceInContext(PopoverPlacementContext input)
    {
        if (!IsPositive(input.LogicalWidth, input.LogicalHeight, input.DpiScale) || !Enum.IsDefined(input.TaskbarEdge)) throw new ArgumentOutOfRangeException(nameof(input));
        var work = input.WorkArea.IsUsable && input.MonitorBounds.IsUsable && Contains(input.MonitorBounds, input.WorkArea)
            ? input.WorkArea : input.PrimaryWorkArea.IsUsable ? input.PrimaryWorkArea : throw new ArgumentOutOfRangeException(nameof(input));
        var width = input.LogicalWidth * input.DpiScale;
        var height = input.LogicalHeight * input.DpiScale;
        if (!IsPositive(width, height)) throw new ArgumentOutOfRangeException(nameof(input));
        width = Math.Min(width, work.Width); height = Math.Min(height, work.Height);
        var cursor = input.Cursor is { IsUsable: true } point ? point : new(work.Left + work.Width / 2, work.Top + work.Height / 2, 1, 1);
        var left = input.TaskbarEdge switch { TaskbarEdge.Left => work.Left, TaskbarEdge.Right => work.Right - width, _ => Clamp(cursor.Left + cursor.Width / 2 - width / 2, work.Left, work.Right - width) };
        var top = input.TaskbarEdge switch { TaskbarEdge.Top => work.Top, TaskbarEdge.Bottom => work.Bottom - height, _ => Clamp(cursor.Top + cursor.Height / 2 - height / 2, work.Top, work.Bottom - height) };
        var result = new PopoverBounds(left / input.DpiScale, top / input.DpiScale, width / input.DpiScale, height / input.DpiScale);
        if (!double.IsFinite(result.Left) || !double.IsFinite(result.Top) || !IsPositive(result.Width, result.Height)) throw new ArgumentOutOfRangeException(nameof(input));
        return result;
    }

    private static bool IsPositive(params double[] values) => values.All(value => double.IsFinite(value) && value > 0);
    private static bool Contains(ScreenRect outer, ScreenRect inner) => inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
    private static double Clamp(double value, double minimum, double maximum) => Math.Clamp(value, minimum, maximum);
}

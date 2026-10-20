using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AIBar.Desktop;

/// <summary>One interruptible panel transition, with cleanup independent of WPF's render clock.</summary>
internal sealed class PanelMotion
{
    private FrameworkElement? _body;
    private Transform? _originalTransform;
    private TranslateTransform? _translation;
    private DispatcherTimer? _deadline;
    private Action? _onCompleted;

    internal static bool CanAnimate(Window window, bool animationEnabled, bool highContrast)
    {
        if (!animationEnabled || highContrast || !window.IsLoaded || !window.IsVisible || !window.ShowActivated)
            return false;
        // RenderTargetBitmap fixtures show their HWND far outside the virtual desktop.
        // Never attach an opacity clock there: it need not receive composition frames.
        if (!double.IsFinite(window.Left) || !double.IsFinite(window.Top) || window.ActualWidth <= 0 || window.ActualHeight <= 0)
            return false;
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var bounds = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        return !bounds.IsEmpty && desktop.IntersectsWith(bounds);
    }

    internal void Begin(FrameworkElement body, bool animate, double offset, int duration, bool vertical = false,
        bool closing = false, Action? onCompleted = null)
    {
        Complete();
        body.BeginAnimation(UIElement.OpacityProperty, null);
        body.SetCurrentValue(UIElement.OpacityProperty, 1d);
        if (!animate) { onCompleted?.Invoke(); return; }
        _onCompleted = onCompleted;
        _body = body;
        _originalTransform = body.RenderTransform;
        _translation = new TranslateTransform();
        var transforms = new TransformGroup();
        transforms.Children.Add(body.RenderTransform);
        transforms.Children.Add(_translation);
        body.RenderTransform = transforms;
        var fade = new DoubleAnimation(closing ? 1 : 0, closing ? 0 : 1, TimeSpan.FromMilliseconds(duration))
        { FillBehavior = FillBehavior.Stop };
        // A dispatcher deadline also runs when there is no compositor frame. Do not
        // depend solely on AnimationClock.Completed (offscreen/minimized/occluded HWND).
        _deadline = new DispatcherTimer(DispatcherPriority.Normal, body.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(duration) };
        _deadline.Tick += OnDeadline;
        _deadline.Start();
        body.BeginAnimation(UIElement.OpacityProperty, fade);
        if (offset != 0)
            _translation.BeginAnimation(vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty,
                new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(duration))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void OnDeadline(object? sender, EventArgs args)
    {
        var onCompleted = _onCompleted;
        Complete();
        onCompleted?.Invoke();
    }

    internal void Complete()
    {
        _onCompleted = null;
        if (_deadline is not null)
        {
            _deadline.Stop();
            _deadline.Tick -= OnDeadline;
            _deadline = null;
        }
        if (_body is null) return;
        var body = _body;
        _body = null;
        body.BeginAnimation(UIElement.OpacityProperty, null);
        body.SetCurrentValue(UIElement.OpacityProperty, 1d);
        _translation?.BeginAnimation(TranslateTransform.XProperty, null);
        _translation?.BeginAnimation(TranslateTransform.YProperty, null);
        body.RenderTransform = _originalTransform ?? Transform.Identity;
        _translation = null;
        _originalTransform = null;
    }
}

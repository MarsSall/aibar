using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AIBar.Domain;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Path = System.Windows.Shapes.Path;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AIBar.Desktop;

public sealed class NotificationWindow : Window
{
    private readonly NotificationDwell _dwell = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Stopwatch _elapsed = new();
    private readonly Border _card;
    private readonly Border _bar;
    private readonly TranslateTransform _slide = new();
    private readonly Action _openPanel;
    private readonly bool _animations;
    private TimeSpan _last;
    private bool _exiting;

    public NotificationWindow(QuotaAlertKind kind, string provider, string title, string body, Action openPanel,
        WindowsTheme? themeOverride = null, bool? animations = null, double backgroundOpacity = 1)
    {
        _openPanel = openPanel;
        _animations = animations ?? SystemParameters.ClientAreaAnimation;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Width = 440; SizeToContent = SizeToContent.Height;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Title = title;
        AutomationProperties.SetName(this, title + ". " + body);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        var source = new WindowsThemeSource();
        var theme = themeOverride ?? source.Current;
        source.Dispose();
        Resources.MergedDictionaries.Add(ThemeDictionaries.Create(theme));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AIBar.Desktop;component/NotificationStyles.xaml") });
        var category = NotificationCategory.For(kind);
        var color = (SolidColorBrush)FindResource(category.Token);
        Brush Token(string key) => (Brush)FindResource(key);
        var background = Token("NotificationSurfaceBrush").CloneCurrentValue();
        if (theme != WindowsTheme.HighContrast && new WindowsDwmSurfaceHints().IsTransparencyEnabled)
            background.Opacity = Math.Clamp(backgroundOpacity, .3, 1);
        _card = new Border
        {
            Margin = new Thickness(40), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Background = background,
            BorderBrush = theme == WindowsTheme.Dark ? new SolidColorBrush(Color.FromArgb(140, color.Color.R, color.Color.G, color.Color.B)) : Token("NotificationBorderBrush"),
            RenderTransform = _slide,
            Effect = theme == WindowsTheme.Dark ? new DropShadowEffect { Color = color.Color, BlurRadius = 28, ShadowDepth = 0, Opacity = .27 } : null
        };
        var grid = new Grid();
        _card.Child = grid;
        var content = new Grid { Margin = new Thickness(14, 12, 44, 14) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(content);
        var codex = string.Equals(provider, "Codex", StringComparison.OrdinalIgnoreCase);
        var tile = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top,
            Background = Token(codex ? "NotificationCodexTileBrush" : "NotificationClaudeTileBrush"),
            Child = new Path { Width = 20, Height = 20, Stretch = Stretch.Uniform, Data = NotificationProviderMarks.For(codex), Fill = Token(codex ? "NotificationCodexBrush" : "NotificationClaudeBrush") }
        };
        if (theme == WindowsTheme.Dark)
        {
            tile.BorderThickness = new Thickness(1);
            var outline = Token(codex ? "NotificationCodexBrush" : "NotificationClaudeBrush").CloneCurrentValue();
            outline.Opacity = .45; tile.BorderBrush = outline;
        }
        var tileSlot = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top };
        tileSlot.Children.Add(tile);
        if (theme != WindowsTheme.Dark)
        {
            tileSlot.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
                BorderBrush = Token("NotificationSurfaceBrush"), Background = color,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransform = new TranslateTransform(4, 4),
                Child = new TextBlock
                {
                    Text = kind == QuotaAlertKind.ThresholdCrossed ? "!" : category.Icon,
                    Foreground = Token("NotificationBadgeTextBrush"), FontSize = 10, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
        content.Children.Add(tileSlot);
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(text, 1); content.Children.Add(text);
        text.Children.Add(new TextBlock { Text = category.Icon + "  " + category.Label + " · NOW", FontSize = 11, LineHeight = 16, FontWeight = FontWeights.SemiBold, Foreground = color });
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, LineHeight = 20, FontWeight = FontWeights.SemiBold, Foreground = Token("NotificationTextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        text.Children.Add(new TextBlock { Text = body, FontSize = 13, LineHeight = 18, Foreground = Token("NotificationSecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var dismiss = new Button
        {
            Style = (Style)FindResource("NotificationDismissStyle"), Content = "×", Width = 28, Height = 28, FontSize = 16, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0)
        };
        AutomationProperties.SetName(dismiss, "Dismiss");
        dismiss.Click += (_, e) => { e.Handled = true; Exit(); };
        grid.Children.Add(dismiss);
        _bar = new Border { Height = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Background = theme == WindowsTheme.Dark ? new LinearGradientBrush(Color.FromArgb(77, color.Color.R, color.Color.G, color.Color.B), color.Color, 0) : Token("NotificationTimerBrush") };
        grid.Children.Add(_bar);
        if (theme == WindowsTheme.Dark)
        {
            grid.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Color.FromArgb(230, color.Color.R, color.Color.G, color.Color.B), .5), new(Colors.Transparent, 1) }, 0) });
        }
        _card.SizeChanged += (_, _) => _bar.Width = Math.Max(0, _card.ActualWidth - 2) * _dwell.Fraction;
        Content = _card;
        SourceInitialized += (_, _) => NotificationNative.Initialize(this, false);
        MouseLeftButtonUp += (_, e) => { if (!e.Handled) Open(); };
        KeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            var duration = TimeSpan.FromMilliseconds(_animations ? 250 : 83);
            _card.BeginAnimation(OpacityProperty, NotificationMotion.Animate(0, 1, duration, false, !_animations));
            if (_animations) _slide.BeginAnimation(TranslateTransform.XProperty, NotificationMotion.Animate(16, 0, duration, false, false));
            // Dwell begins only once entry has finished.
            _last = -duration; _elapsed.Start(); _timer.Start();
            var peer = UIElementAutomationPeer.CreatePeerForElement(this);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        };
        _timer.Tick += Tick;
        Closed += (_, _) =>
        {
            _exiting = true; _timer.Stop(); _timer.Tick -= Tick; _elapsed.Stop();
            _card.BeginAnimation(OpacityProperty, null);
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
        };
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_exiting) return;
        var now = _elapsed.Elapsed;
        if (_last < TimeSpan.Zero)
        {
            if (now < -_last) return;
            _last = now;
        }
        _dwell.Advance(now - _last, _card.IsMouseOver, IsKeyboardFocusWithin);
        _last = now;
        _bar.Width = Math.Max(0, _card.ActualWidth - 2) * _dwell.Fraction;
        if (_dwell.Expired) Exit();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource is not Button) { e.Handled = true; Open(); }
        else if (e.Key == Key.Escape) { e.Handled = true; Exit(); }
    }
    private void Open() { if (_exiting) return; _openPanel(); Exit(); }
    private void Exit()
    {
        if (_exiting) return;
        _exiting = true; _timer.Stop();
        var duration = TimeSpan.FromMilliseconds(_animations ? 167 : 83);
        var fade = NotificationMotion.Animate(_card.Opacity, 0, duration, true, !_animations);
        fade.Completed += (_, _) => Close();
        _card.BeginAnimation(OpacityProperty, fade);
        if (_animations) _slide.BeginAnimation(TranslateTransform.XProperty, NotificationMotion.Animate(_slide.X, 8, duration, true, false));
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new WindowAutomationPeer(this);
}

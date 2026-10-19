using System.Windows;
// The Desktop project also references WinForms; pin the WPF control type.
using UserControl = System.Windows.Controls.UserControl;

namespace AIBar.Desktop;

/// <summary>One CodexBar-style window: title, brand bar, usage/reset and inline pace.</summary>
public partial class PanelMeter : UserControl
{
    public static readonly DependencyProperty ProviderProperty = Register(nameof(Provider), "Codex");
    public static readonly DependencyProperty PaceProperty = Register<PanelPacePresentation?>(nameof(Pace), null);
    public string Provider { get => (string)GetValue(ProviderProperty); set => SetValue(ProviderProperty, value); }
    public PanelPacePresentation? Pace { get => (PanelPacePresentation?)GetValue(PaceProperty); set => SetValue(PaceProperty, value); }

    public static readonly DependencyProperty LabelProperty = Register<string?>(nameof(Label), null);
    public static readonly DependencyProperty UsageProperty = Register<decimal?>(nameof(Usage), null);
    public static readonly DependencyProperty IsCurrentProperty = Register(nameof(IsCurrent), true);
    public static readonly DependencyProperty ResetCountdownProperty = Register<string?>(nameof(ResetCountdown), null);
    public static readonly DependencyProperty MeterNameProperty = Register<string?>(nameof(MeterName), null);

    public PanelMeter() => InitializeComponent();

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public decimal? Usage { get => (decimal?)GetValue(UsageProperty); set => SetValue(UsageProperty, value); }
    public bool IsCurrent { get => (bool)GetValue(IsCurrentProperty); set => SetValue(IsCurrentProperty, value); }
    public string? ResetCountdown { get => (string?)GetValue(ResetCountdownProperty); set => SetValue(ResetCountdownProperty, value); }
    public string? MeterName { get => (string?)GetValue(MeterNameProperty); set => SetValue(MeterNameProperty, value); }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(PanelMeter), new PropertyMetadata(defaultValue));
}

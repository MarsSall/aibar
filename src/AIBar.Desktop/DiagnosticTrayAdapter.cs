using AIBar.Application;

namespace AIBar.Desktop;

/// <summary>Translates trusted tray actions into the private diagnostic command capability without exposing sinks to the UI.</summary>
internal sealed class DiagnosticTrayAdapter
{
    private readonly DiagnosticCommand _command;
    internal DiagnosticTrayAdapter(DiagnosticCommand command) => _command = command ?? throw new ArgumentNullException(nameof(command));
    internal DiagnosticPreview HandleTrustedGesture() => _command.BeginTrustedGesture();
    internal DiagnosticCommandResult Confirm(DiagnosticPreview preview, DiagnosticCategory category) => _command.Confirm(preview, category);
    internal void Cancel(DiagnosticPreview preview) => _command.Cancel(preview);
}

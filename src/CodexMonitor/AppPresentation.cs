using System.Text.Json;
using CodexMonitor.Core;

namespace CodexMonitor;

internal static class AppPresentation
{
    // One version source for the UI, handshake and diagnostics.
    public static string Version => typeof(AppPresentation).Assembly.GetName().Version!.ToString(3);
    public static string VersionLabel => $"CodexMonitor {Version}";
    public static string AboutMessage => Localization.Format(nameof(UiText.AboutBody), Version);

    public static string ErrorMessage(Exception error) => error switch
    {
        FileNotFoundException => UiText.CliMissing,
        OperationCanceledException => UiText.Timeout,
        AppServerException serverError => serverError.Message,
        JsonException => UiText.InvalidResponse,
        _ => UiText.ConnectionUnavailable
    };
}

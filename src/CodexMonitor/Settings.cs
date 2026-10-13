using System.Text.Json;
using Microsoft.Win32;

namespace CodexMonitor;

public sealed record Settings(string? SelectedQuota = null)
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMonitor", "settings.json");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, FilePath, true);
    }
}

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string Command => $"\"{Environment.ProcessPath}\"";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("CodexMonitor") is string value && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
    }
    // Only called after the user clicks the checkbox. Never run during initialization or tests.
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled) key.SetValue("CodexMonitor", Command, RegistryValueKind.String);
        else key.DeleteValue("CodexMonitor", false);
    }
}

using CodexMonitor.Core;

namespace CodexMonitor;

public sealed record CodexCommand(string Executable, IReadOnlyList<string> Arguments)
{
    public static CodexCommand Native(string executable) => new(executable, ["app-server", "--listen", "stdio://"]);

    // Resolve in the Linux login environment, never through cmd.exe or PowerShell.
    // Keep profile output on stderr so stdout contains only protocol messages.
    private const string WslServer = """
        exec 1>&3 3>&-
        cli="$1"
        if [ -z "$cli" ]; then cli=$(command -v codex) || exit 127; fi
        case "$cli" in /*) ;; *) exit 127 ;; esac
        case "$cli" in *.exe|*.cmd|*.ps1) exit 127 ;; esac
        [ -f "$cli" ] && [ -x "$cli" ] || exit 127
        exec "$cli" app-server --listen stdio://
        """;

    internal static CodexCommand Wsl(string launcher, string? distribution, string? cliPath)
    {
        if (!string.IsNullOrWhiteSpace(cliPath) && !cliPath.StartsWith('/'))
            throw new FileNotFoundException(UiText.InvalidWslCliPath);
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(distribution)) arguments.AddRange(["--distribution", distribution]);
        arguments.AddRange(["--cd", "~", "--exec", "/bin/sh", "-c",
            "unset CODEX_HOME; exec 3>&1; exec /bin/sh -lc '" + WslServer.Replace("'", "'\"'\"'") + "' codex-monitor \"$@\" 1>&2",
            "codex-monitor", cliPath ?? ""]);
        return new(launcher, arguments);
    }
}

public static class CodexLocation
{
    public static CodexCommand Find(bool allowWslFallback = true) => Find(allowWslFallback,
        Environment.GetEnvironmentVariable("CODEX_MONITOR_CODEX_PATH"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe"),
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"),
        Environment.GetEnvironmentVariable("CODEX_MONITOR_WSL_DISTRIBUTION"),
        Environment.GetEnvironmentVariable("CODEX_MONITOR_WSL_CODEX_PATH"), File.Exists);

    internal static CodexCommand Find(bool allowWslFallback, string? overridePath, string native,
        string searchPath, string wsl, string? distribution, string? wslCliPath, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (Path.IsPathFullyQualified(overridePath) && exists(overridePath) && overridePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return CodexCommand.Native(overridePath);
            // An explicit Windows override must never silently select another account.
            throw new FileNotFoundException(UiText.InvalidCliPath);
        }
        if (exists(native)) return CodexCommand.Native(native);
        foreach (var folder in searchPath.Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(folder)) continue;
            try
            {
                var candidate = Path.Combine(folder, "codex.exe");
                if (exists(candidate)) return CodexCommand.Native(candidate);
            }
            catch (ArgumentException) { }
        }
        if (allowWslFallback && exists(wsl)) return CodexCommand.Wsl(wsl, distribution, wslCliPath);
        throw new FileNotFoundException(UiText.CliMissing);
    }
}

using CodexMonitor;
using CodexMonitor.Core;

internal static class WslFallbackTests
{
    public static void Run(Action<string, Action> test)
    {
        const string native = @"C:\Local\Codex\codex.exe";
        const string pathCli = @"C:\Tools\Codex\codex.exe";
        const string wsl = @"C:\Windows\System32\wsl.exe";
        CodexCommand Find(bool enabled, string? custom, params string[] files) => CodexLocation.Find(
            enabled, custom, native, @"relative;C:\Tools\Codex", wsl, null, null, files.Contains);
        test("Windows CLI and explicit override take priority over WSL", () =>
        {
            Equal(native, Find(true, null, native, pathCli, wsl).Executable);
            Equal(pathCli, Find(true, null, pathCli, wsl).Executable);
            Equal(pathCli, Find(true, pathCli, native, pathCli, wsl).Executable);
            Equal("app-server|--listen|stdio://", string.Join('|', Find(true, null, native).Arguments));
        });
        test("WSL requires missing Windows CLI, an enabled fallback and a launcher", () =>
        {
            Equal(wsl, Find(true, null, wsl).Executable);
            Missing(() => Find(false, null, wsl));
            Missing(() => Find(true, null));
            Missing(() => Find(true, @"C:\Missing\codex.exe", wsl));
            Missing(() => Find(true, "relative.exe", wsl));
            Missing(() => Find(true, @"C:\Tools\codex.cmd", @"C:\Tools\codex.cmd", wsl));
        });
        test("WSL options are literal arguments, with home working directory and Linux path validation", () =>
        {
            const string distribution = "Ubuntu; echo TEST_SECRET";
            const string cli = "/home/user/Codex's folder/$(echo TEST_SECRET)/codex";
            var command = CodexCommand.Wsl(wsl, distribution, cli);
            Equal(distribution, command.Arguments[1]);
            Equal("--cd", command.Arguments[2]); Equal("~", command.Arguments[3]);
            Equal(cli, command.Arguments[^1]);
            True(!command.Arguments[^3].Contains(cli), "Linux path was interpolated into shell code");
            Missing(() => CodexCommand.Wsl(wsl, null, "codex"));
            Missing(() => CodexCommand.Wsl(wsl, null, @"C:\Codex\codex.exe"));
            // Invalid WSL configuration cannot block an available Windows installation.
            Equal(native, CodexLocation.Find(true, null, native, "", wsl, null, "relative", p => p == native).Executable);
        });
        test("old settings enable WSL fallback and both saved choices preserve language and quota", () =>
        {
            var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "verification", "wsl-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            try
            {
                File.WriteAllText(path, """{"SelectedQuota":"codex/secondary","Language":"fr"}""");
                var old = Settings.Load(path);
                True(old.AllowWslFallback, "Missing preference should enable fallback");
                foreach (var enabled in new[] { false, true })
                {
                    var updated = old with { AllowWslFallback = enabled };
                    updated.Save(path);
                    Equal(updated, Settings.Load(path));
                }
            }
            finally { Directory.Delete(directory, true); }
        });
        test("WSL menu checkbox invokes its callback and relocalizes without losing state", () =>
        {
            using var scope = new LocalizationTests.CultureScope("fr-FR");
            using var menu = new System.Windows.Forms.ContextMenuStrip();
            TrayMenu.Items? items = null;
            var clicks = 0;
            items = TrayMenu.Populate(menu, () => { }, () => { }, () => { }, () => { }, () => { },
                toggleWslFallback: () => { clicks++; items!.WslFallback.Checked = !items.WslFallback.Checked; }, allowWslFallback: false);
            True(!items.WslFallback.Checked, "Incorrect persisted checkbox");
            items.WslFallback.PerformClick();
            Equal(1, clicks);
            Localization.ApplyPreference(LanguagePreference.English);
            TrayMenu.Relocalize(items, LanguagePreference.English);
            Equal("Use WSL when the Windows CLI is missing", items.WslFallback.Text);
            True(items.WslFallback.Checked, "Relocalizing lost checkbox state");
        });
    }

    private static void Missing(Func<CodexCommand> find)
    {
        try { find(); throw new InvalidOperationException("Expected missing CLI"); }
        catch (FileNotFoundException) { }
    }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, actual {actual}"); }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

using System.Diagnostics;
using System.Text;
using CodexMonitor;
using CodexMonitor.Core;

internal static class WslIntegrationTests
{
    public static async Task<int> RunAsync(bool live)
    {
        var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "verification", "wsl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Exercise actual Windows -> WSL argument handling, including shell metacharacters.
            var fixture = Path.Combine(directory, "Codex's $(echo INJECTION) fixture");
            File.WriteAllText(fixture, """
                #!/bin/sh
                echo $$ > "$0.pid"
                while IFS= read -r line; do
                    case "$line" in
                        *'"method":"initialize"'*) echo '{"id":1,"result":{"userAgent":"fixture"}}' ;;
                        *'"method":"initialized"'*) ;;
                        *'"method":"account/rateLimits/read"'*)
                            echo '{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":13}}}}' ;;
                        *) exit 90 ;;
                    esac
                done
                """.ReplaceLineEndings("\n") + "\n", new UTF8Encoding(false));
            var linuxFixture = LinuxPath(fixture);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
            using (var client = await AppServerClient.ConnectAsync(CodexCommand.Wsl(wsl, null, linuxFixture), timeout.Token))
            {
                var quota = UsageSnapshot.Parse(await client.ReadQuotaAsync(timeout.Token), DateTimeOffset.UtcNow);
                Require(quota.Select(null)?.BadgeValue == 87, "WSL fixture percentage");
            }
            var linuxPid = File.ReadAllText(fixture + ".pid").Trim();
            Require(await LinuxExited(wsl, linuxPid), "Linux fixture survived client disposal");
            Console.WriteLine("PASS actual WSL stdio transport, literal CLI path and Linux server EOF cleanup");

            using (var owner = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--wsl-job-owner", fixture }
            })!)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    while (!File.Exists(fixture + ".owner")) await Task.Delay(100, timeout.Token);
                    linuxPid = File.ReadAllText(fixture + ".pid").Trim();
                    owner.Kill(); await owner.WaitForExitAsync(timeout.Token);
                    Require(await LinuxExited(wsl, linuxPid), "Linux fixture survived monitor crash");
                    Console.WriteLine("PASS Linux server cleanup after abrupt Windows owner exit");
                }
                finally { if (!owner.HasExited) owner.Kill(true); }
            }

            var blocked = Path.Combine(directory, "blocked-cli");
            File.WriteAllText(blocked, "#!/bin/sh\necho $$ > \"$0.pid\"\nwhile IFS= read -r line; do :; done\n", new UTF8Encoding(false));
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                try
                {
                    using var client = await AppServerClient.ConnectAsync(CodexCommand.Wsl(wsl, null, LinuxPath(blocked)), timeout.Token);
                    throw new InvalidOperationException("WSL initialization should time out");
                }
                catch (OperationCanceledException) { }
            }
            Require(await LinuxExited(wsl, File.ReadAllText(blocked + ".pid").Trim()), "Linux fixture survived canceled initialization");
            Console.WriteLine("PASS WSL initialization cancellation cleans up the Linux process");

            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    using var client = await AppServerClient.ConnectAsync(CodexCommand.Wsl(wsl, null, "/nonexistent/codex-monitor-cli"), timeout.Token);
                    throw new InvalidOperationException("Missing Linux CLI should fail");
                }
                catch (IOException) { }
            }
            Console.WriteLine("PASS missing Linux CLI fails without starting a model turn");

            if (live)
            {
                // Simulate a Windows-only absence without changing installations or user settings.
                var command = CodexLocation.Find(true, null, @"C:\Missing\codex.exe", "", wsl,
                    Environment.GetEnvironmentVariable("CODEX_MONITOR_WSL_DISTRIBUTION"),
                    Environment.GetEnvironmentVariable("CODEX_MONITOR_WSL_CODEX_PATH"), p => p == wsl && File.Exists(p));
                var pidPath = Path.Combine(directory, "live.pid");
                var arguments = command.Arguments.ToList();
                arguments[arguments.IndexOf("-c") + 1] = "echo $$ > \"$2\"; " + arguments[arguments.IndexOf("-c") + 1];
                arguments.Add(LinuxPath(pidPath));
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                using (var client = await AppServerClient.ConnectAsync(command with { Arguments = arguments }, timeout.Token))
                {
                    var quota = UsageSnapshot.Parse(await client.ReadQuotaAsync(timeout.Token), DateTimeOffset.UtcNow);
                    Require(quota.Windows.Any(w => w.Remaining is not null), "No real WSL quota percentage returned");
                }
                Require(await LinuxExited(wsl, File.ReadAllText(pidPath).Trim()), "Real Linux App Server survived disposal");
                Console.WriteLine("PASS live quota via WSL fallback with Windows CLI absence simulated, and real server cleanup");
            }
        }
        finally { Directory.Delete(directory, true); }
        return live ? 5 : 4;
    }

    internal static string LinuxPath(string path) => "/mnt/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/');
    private static async Task<bool> LinuxExited(string wsl, string pid)
    {
        Require(int.TryParse(pid, out var value) && value > 0, "Invalid fixture PID");
        using var probe = Process.Start(new ProcessStartInfo(wsl)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "--exec", "/bin/sh", "-c",
                "for i in 1 2 3 4 5 6 7 8 9 10; do kill -0 \"$1\" 2>/dev/null || exit 0; sleep 0.2; done; exit 1",
                "codex-monitor-test", pid }
        })!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await probe.WaitForExitAsync(timeout.Token);
        return probe.ExitCode == 0;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

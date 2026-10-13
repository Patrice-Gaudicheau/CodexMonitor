using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using CodexMonitor;
using CodexMonitor.Core;

internal static class Program
{
    private static int passed;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("app-server")) return await FakeServer();
        if (args.Length == 2 && args[0] == "--job-owner")
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var ownerClient = await AppServerClient.ConnectAsync(Environment.ProcessPath!, cancellation.Token);
            File.WriteAllText(args[1], ownerClient.ProcessId.ToString());
            await Task.Delay(TimeSpan.FromSeconds(30));
            return 0;
        }
        try
        {
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            // The existing French suite remains a deterministic reference on any Windows language.
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            LocalizationTests.Run(Test);
            LanguageOverrideTests.Run(Test);
            Test("remaining values 100/87/50/9/0", () =>
            {
                foreach (var remaining in new[] { 100, 87, 50, 9, 0 })
                {
                    var snapshot = Parse(JsonSerializer.Serialize(new { rateLimits = new { primary = new { usedPercent = 100 - remaining, windowDurationMins = 300 } } }));
                    Equal(remaining, UsagePresentation.DisplayValue(snapshot, null, Now, false));
                }
            });
            Test("threshold colors", () =>
            {
                Equal(BadgeTone.Green, UsagePresentation.Tone(51));
                Equal(BadgeTone.Orange, UsagePresentation.Tone(50));
                Equal(BadgeTone.Orange, UsagePresentation.Tone(20));
                Equal(BadgeTone.Red, UsagePresentation.Tone(19));
                Equal(BadgeTone.Red, UsagePresentation.Tone(0));
                Equal(BadgeTone.Neutral, UsagePresentation.Tone(null));
            });
            Test("missing is unavailable, not zero", () =>
            {
                foreach (var json in new[] { "{}", "{\"rateLimits\":null}", "{\"rateLimits\":{\"primary\":{}}}", "{\"rateLimits\":{\"primary\":{\"usedPercent\":null}}}" })
                    Equal<int?>(null, UsagePresentation.DisplayValue(Parse(json), null, Now, false));
            });
            Test("window identity, map authority, 5h preference", () =>
            {
                var snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":2,"windowDurationMins":300}},"codex":{"primary":{"usedPercent":13,"windowDurationMins":10080},"secondary":{"usedPercent":44,"windowDurationMins":300}}}}""");
                Equal(3, snapshot.Windows.Count);
                Equal("codex/secondary", snapshot.Select(null)?.Key);
                Equal(87, snapshot.Select("codex/primary")?.BadgeValue);
                Equal<QuotaWindow?>(null, snapshot.Select("disappeared/primary"));
                Equal(0, Parse("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{}}""").Windows.Count);
                Equal(1, Parse("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":null}""").Windows.Count);
            });
            Test("weekly-only is labeled by duration, not primary slot", () =>
            {
                var snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":13,"windowDurationMins":10080}}}""");
                Equal("Semaine", snapshot.Select(null)?.DurationLabel);
                Equal(87, snapshot.Select(null)?.BadgeValue);
            });
            Test("staleness, failure, reset and recovery", () =>
            {
                var snapshot = Parse(JsonSerializer.Serialize(new { rateLimits = new { primary = new { usedPercent = 13, resetsAt = Now.AddMinutes(2).ToUnixTimeSeconds() } } }));
                Equal(87, UsagePresentation.DisplayValue(snapshot, null, Now.AddSeconds(119), false));
                Equal<int?>(null, UsagePresentation.DisplayValue(snapshot, null, Now.AddMinutes(2), false));
                Equal<int?>(null, UsagePresentation.DisplayValue(snapshot, null, Now, true));
                Equal(87, UsagePresentation.DisplayValue(snapshot, null, Now, false));
                var noReset = Parse("""{"rateLimits":{"primary":{"usedPercent":13}}}""");
                Equal<int?>(null, UsagePresentation.DisplayValue(noReset, null, Now.AddMinutes(3), false));
                Equal<int?>(null, UsagePresentation.DisplayValue(noReset, null, Now.AddSeconds(-1), false));
            });
            Test("defensive numbers, unknown fields, invalid resets", () =>
            {
                Equal(0, Parse("""{"rateLimits":{"primary":{"usedPercent":120}}}""").Select(null)?.BadgeValue);
                Equal(100, Parse("""{"rateLimits":{"primary":{"usedPercent":-5}}}""").Select(null)?.BadgeValue);
                Equal(86, Parse("""{"rateLimits":{"primary":{"usedPercent":13.1,"unknown":true}}}""").Select(null)?.BadgeValue);
                var invalid = Parse("""{"rateLimits":{"primary":{"usedPercent":"13","windowDurationMins":{},"resetsAt":9223372036854775807}}}""");
                Equal<int?>(null, invalid.Windows[0].BadgeValue);
                Equal<DateTimeOffset?>(null, invalid.Windows[0].ResetsAt);
                var malformed = Parse("""{"rateLimitsByLimitId":{"bad":false,"good":{"secondary":{"usedPercent":40}}}}""");
                Equal(1, malformed.Windows.Count);
            });
            Test("credits aren't percentages; individual remaining is direct", () =>
            {
                var snapshot = Parse("""{"rateLimits":{"credits":{"hasCredits":true,"unlimited":true,"balance":"100"},"individualLimit":{"remainingPercent":37,"resetsAt":1800003600}},"ordinaryUsageAllowed":false}""");
                Equal(37, snapshot.Windows[0].BadgeValue);
                Equal(2, snapshot.Notes.Count);
            });
            Test("tooltips fit Windows limit and retain reset", () =>
            {
                var snapshot = Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":13,"windowDurationMins":300,"resetsAt":1800003600},"secondary":{"usedPercent":50,"windowDurationMins":10080,"resetsAt":1800007200}},"other":{"primary":{"usedPercent":20}}}}""");
                var tooltip = UsagePresentation.Tooltip(snapshot, null, Now, false);
                True(tooltip.Length <= 127, $"Tooltip length {tooltip.Length}");
                True(tooltip.Contains("87%"), "Remaining missing");
                True(tooltip.Contains(snapshot.Windows[0].ResetLabel), "Reset missing");
                True(!UsagePresentation.Tooltip(snapshot, null, Now, true).Contains("87%"), "Failure kept obsolete percentage");
            });
            Test("badge rendering at 100/125/150/200/250/300 percent scale", () =>
            {
                var output = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "verification", "icons");
                Directory.CreateDirectory(output);
                var sizes = new[] { 16, 20, 24, 32, 40, 48 };
                var values = new int?[] { 100, 87, 50, 9, 0, null };
                using var contact = new Bitmap(690, 430);
                using var g = Graphics.FromImage(contact);
                g.Clear(Color.White);
                using var font = new Font("Segoe UI", 10);
                for (var row = 0; row < sizes.Length; row++)
                {
                    g.DrawString($"{sizes[row]} px", font, Brushes.Black, 2, row * 70 + 24);
                    for (var column = 0; column < values.Length; column++)
                    {
                        using var badge = BadgeRenderer.Render(values[column], UsagePresentation.Tone(values[column]), sizes[row]);
                        Equal(sizes[row], badge.Width);
                        True(badge.GetPixel(0, 0).A == 0, "Opaque corner");
                        g.DrawImageUnscaled(badge, 95 + column * 95, row * 70 + 12);
                        badge.Save(Path.Combine(output, $"badge-{sizes[row]}-{values[column]?.ToString() ?? "missing"}.png"));
                        using var icon = BadgeRenderer.Create(values[column], UsagePresentation.Tone(values[column]), sizes[row]);
                        Equal(sizes[row], icon.Width);
                    }
                }
                contact.Save(Path.Combine(output, "contact-sheet.png"));
            });
            Test("HICON and GDI handles are released", () =>
            {
                using var process = Process.GetCurrentProcess();
                var before = GetGuiResources(process.Handle, 0);
                for (var i = 0; i < 1500; i++) { using var icon = BadgeRenderer.Create(i % 101, BadgeTone.Green, 16); }
                var after = GetGuiResources(process.Handle, 0);
                True(after <= before + 5, $"GDI leak: {before} -> {after}");
            });
            Test("details preserve selected quota, freshness and real timestamps", () =>
            {
                var snapshot = Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":13,"windowDurationMins":300,"resetsAt":1800003600},"secondary":{"usedPercent":50,"windowDurationMins":10080,"resetsAt":1800007200}}}}""");
                var model = DetailsViewModel.Create(snapshot, "codex/secondary", Now, false, null, false);
                Equal("Semaine", model.QuotaLabel);
                Equal(50, model.Percentage);
                Equal(50d, model.Remaining);
                Equal(1, model.OtherQuotas.Count);
                True(model.OtherQuotas[0].Value.Contains("87"), "Other quota lost");
                True(model.ResetDate.Contains("2027"), "Reset date lost");
                var missing = DetailsViewModel.Create(snapshot, "missing/primary", Now, false, null, false);
                Equal<int?>(null, missing.Percentage);
                var failed = DetailsViewModel.Create(snapshot, null, Now, true, "Connexion indisponible", false);
                Equal<int?>(null, failed.Percentage);
                Equal<double?>(null, failed.Remaining);
                True(failed.OtherQuotas.All(q => q.Value == "Indisponible"), "Obsolete other quota shown");
                True(failed.ReadTime != "En attente de Codex", "Last successful read was discarded");
                var stale = DetailsViewModel.Create(snapshot, null, Now.AddMinutes(3), false, null, false);
                Equal<int?>(null, stale.Percentage);
                Equal<double?>(null, stale.Remaining);
                Equal(BadgeTone.Neutral, stale.Tone);
            });
            Test("details native refresh action and visual states at scaled DPI", () =>
            {
                var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "verification", "details-redesign");
                Directory.CreateDirectory(output);
                var snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":13,"windowDurationMins":10080,"resetsAt":1800003600},"credits":{"hasCredits":false,"unlimited":false,"balance":"0"}}}""");
                var model = DetailsViewModel.Create(snapshot, null, Now, false, null, false);
                using (var form = new DetailsForm())
                {
                    form.UpdateUsage(model);
                    var clicks = 0;
                    form.RefreshRequested += () => clicks++;
                    var button = form.Controls.OfType<System.Windows.Forms.Panel>().SelectMany(p => p.Controls.OfType<System.Windows.Forms.Button>()).Single();
                    form.Show(); form.Refresh();
                    button.PerformClick(); Equal(1, clicks);
                    form.UpdateUsage(model with { Refreshing = true });
                    True(!button.Enabled, "Busy refresh button still enabled");
                    button.PerformClick(); Equal(1, clicks);
                    form.UpdateUsage(model);
                    using var actual = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                    form.DrawToBitmap(actual, form.ClientRectangle);
                    actual.Save(Path.Combine(output, "native-form.png"));
                    form.Close();
                }
                foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
                {
                    foreach (var remaining in new int?[] { 100, 87, 50, 9, 0, null })
                    {
                        var valueSnapshot = remaining is { } value ? Parse(JsonSerializer.Serialize(new { rateLimits = new { primary = new { usedPercent = 100 - value, windowDurationMins = 10080, resetsAt = 1800003600 } } })) : null;
                        var state = DetailsViewModel.Create(valueSnapshot, null, Now, false, null, false);
                        using var surface = new DetailsSurface { Model = state };
                        using var bitmap = new Bitmap((int)(520 * scale), (int)(650 * scale));
                        using var graphics = Graphics.FromImage(bitmap);
                        graphics.Clear(Color.FromArgb(244, 248, 247));
                        graphics.ScaleTransform(scale, scale);
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        var height = surface.Draw(graphics, 520, true);
                        True(height <= 650, "Default details content clipped");
                        bitmap.Save(Path.Combine(output, $"details-{scale:0.##}-{remaining?.ToString() ?? "missing"}.png"));
                    }
                }
            });
            Test("details fit all content at native 96/120/144 DPI; scroll only with constrained height", () =>
            {
                var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "verification", "ui-polish");
                Directory.CreateDirectory(output);
                var snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":13,"windowDurationMins":10080,"resetsAt":1800003600},"credits":{"hasCredits":false,"unlimited":false,"balance":"0"}}}""");
                var model = DetailsViewModel.Create(snapshot, null, Now, false, null, false);
                var observations = new List<object>();
                foreach (var dpi in new[] { 96, 120, 144 })
                {
                    using var form = new DetailsForm();
                    form.UpdateUsage(model);
                    form.Show();
                    if (form.DeviceDpi != dpi)
                    {
                        var scale = dpi / (double)form.DeviceDpi;
                        var bounds = form.Bounds;
                        var suggested = new DpiRect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Left + (int)(bounds.Width * scale), Bottom = bounds.Top + (int)(bounds.Height * scale) };
                        // Exercise WinForms' real WM_DPICHANGED handling, without changing global display settings.
                        SendMessage(form.Handle, 0x2E0, new IntPtr(dpi | (dpi << 16)), ref suggested);
                        // Windows normally updates each child HWND's DPI on the new monitor.
                        // The test keeps the real monitor unchanged, so seed that per-control
                        // cache explicitly in this fixture only, after WinForms scales the form.
                        SetFixtureChildDpi(form, dpi);
                    }
                    System.Windows.Forms.Application.DoEvents();
                    Equal(dpi, form.DeviceDpi);
                    form.ClientSize = new Size((int)(520 * dpi / 96f), form.ClientSize.Height);
                    form.FitContent(new Rectangle(0, 0, 4000, 1600));
                    System.Windows.Forms.Application.DoEvents();
                    var scroll = form.Controls.OfType<System.Windows.Forms.Panel>().Single(p => p.AutoScroll);
                    var surface = scroll.Controls.OfType<DetailsSurface>().Single();
                    Equal(dpi, surface.DeviceDpi);
                    True(!scroll.VerticalScroll.Visible, $"Unnecessary scrollbar at {dpi} DPI: content {surface.Height}, viewport {scroll.ClientSize.Height}");
                    True(surface.Top >= 0 && surface.Bottom <= scroll.ClientSize.Height, "Content clipped without scrollbar");
                    var button = form.Controls.OfType<System.Windows.Forms.Panel>().SelectMany(p => p.Controls.OfType<System.Windows.Forms.Button>()).Single();
                    True(button.Height > 0 && button.Parent!.Bottom <= form.ClientSize.Height, "Refresh button clipped");
                    observations.Add(new { dpi, scalePercent = dpi * 100 / 96, contentHeight = surface.Height, viewportHeight = scroll.ClientSize.Height, scrollbar = scroll.VerticalScroll.Visible });
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(Path.Combine(output, $"window-{dpi}.png"));
                    }
                    form.FitContent(new Rectangle(0, 0, 4000, 720));
                    System.Windows.Forms.Application.DoEvents();
                    True(form.Height <= 688, "Small-screen window extends outside working area");
                    True(scroll.VerticalScroll.Visible, "Required scrolling unavailable on a smaller screen");
                    scroll.AutoScrollPosition = new Point(0, surface.Height);
                    System.Windows.Forms.Application.DoEvents();
                    True(surface.Bottom <= scroll.ClientSize.Height, "Last content line is not reachable");
                    form.FitContent(new Rectangle(0, 0, 4000, 1600));
                    System.Windows.Forms.Application.DoEvents();
                    True(!scroll.VerticalScroll.Visible, "Scrollbar persisted after sufficient height restored");
                    form.Close();
                }
                File.WriteAllText(Path.Combine(output, "dpi-layout.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
            });
            // The console test runner has no message loop after closing its temporary native form.
            SynchronizationContext.SetSynchronizationContext(null);
            var oldMode = Environment.GetEnvironmentVariable("CODEX_MONITOR_TEST_SERVER_MODE");
            try
            {
                foreach (var culture in new[] { "fr-FR", "en-US" })
                foreach (var mode in new[] { "normal", "malformed", "error", "hang", "exit", "callback" })
                {
                    using var scope = new LocalizationTests.CultureScope(culture);
                    Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_SERVER_MODE", mode);
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var fixture = await AppServerClient.ConnectAsync(Environment.ProcessPath!, cancellation.Token);
                    var succeeded = false;
                    try
                    {
                        var result = await fixture.ReadQuotaAsync(cancellation.Token);
                        Equal(87, UsageSnapshot.Parse(result, Now).Select(null)?.BadgeValue);
                        succeeded = true;
                    }
                    catch (Exception e) when (e is IOException or JsonException or AppServerException or OperationCanceledException)
                    {
                        True(!e.Message.Contains("TEST_SECRET"), "Upstream secret escaped into error message");
                        if (mode == "error") Equal(Localization.Format(nameof(UiText.ServerRefused), 401), e.Message);
                    }
                    Equal(mode is "normal" or "callback", succeeded);
                    passed++; Console.WriteLine($"PASS app-server transport {culture} {mode}");
                }
                Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_SERVER_MODE", "normal");
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    using var fixture = await AppServerClient.ConnectAsync(Environment.ProcessPath!, cancellation.Token);
                    using var serverProcess = Process.GetProcessById(fixture.ProcessId);
                    serverProcess.Kill(true); serverProcess.WaitForExit();
                    try { await fixture.ReadQuotaAsync(cancellation.Token); throw new InvalidOperationException("Read succeeded after server exit"); }
                    catch (Exception e) when (e is IOException or OperationCanceledException) { }
                    using var replacement = await AppServerClient.ConnectAsync(Environment.ProcessPath!, cancellation.Token);
                    Equal(87, UsageSnapshot.Parse(await replacement.ReadQuotaAsync(cancellation.Token), Now).Select(null)?.BadgeValue);
                    passed++; Console.WriteLine("PASS abrupt server exit and reconnect");
                }
                var pidFile = Path.Combine(Path.GetTempPath(), "CodexMonitor-job-" + Guid.NewGuid() + ".txt");
                using (var owner = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--job-owner", pidFile }, UseShellExecute = false, CreateNoWindow = true })!)
                {
                    try
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(10);
                        while (!File.Exists(pidFile) && DateTime.UtcNow < deadline) await Task.Delay(100);
                        True(File.Exists(pidFile), "Job owner failed to start");
                        var childPid = int.Parse(File.ReadAllText(pidFile));
                        owner.Kill(); owner.WaitForExit(); // Deliberately not Kill(entireProcessTree): the job must terminate the child.
                        for (var i = 0; i < 30 && ProcessExists(childPid); i++) await Task.Delay(100);
                        True(!ProcessExists(childPid), "Orphan server after owner crash");
                        passed++; Console.WriteLine("PASS kill-on-close job prevents orphans after owner crash");
                    }
                    finally { if (!owner.HasExited) owner.Kill(true); if (File.Exists(pidFile)) File.Delete(pidFile); }
                }
            }
            finally { Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_SERVER_MODE", oldMode); }
            if (args.Contains("--live"))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var client = await AppServerClient.ConnectAsync(CodexLocation.Find(), timeout.Token);
                var pid = client.ProcessId;
                using (client)
                {
                    var result = await client.ReadQuotaAsync(timeout.Token);
                    var snapshot = UsageSnapshot.Parse(result, DateTimeOffset.UtcNow);
                    True(snapshot.Windows.Any(w => w.Remaining is not null), "No live percentage returned");
                    Console.WriteLine("LIVE quota retrieved: " + string.Join(", ", snapshot.Windows.Select(w => $"{w.Label} = {w.BadgeValue}%")));
                }
                True(!ProcessExists(pid), "Orphan app-server after disposal");
                passed++;
            }
            Console.WriteLine($"PASS: {passed} test groups");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static UsageSnapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return UsageSnapshot.Parse(doc.RootElement, Now); }
    private static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, actual {actual}"); }
    private static bool ProcessExists(int pid) { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct DpiRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, ref DpiRect bounds);
    private static void SetFixtureChildDpi(System.Windows.Forms.Control control, int dpi)
    {
        var property = typeof(System.Windows.Forms.Control).GetProperty("DeviceDpiInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DPI fixture needs updating for this WinForms runtime.");
        foreach (System.Windows.Forms.Control child in control.Controls)
        {
            property.SetValue(child, dpi);
            SetFixtureChildDpi(child, dpi);
        }
    }

    private static async Task<int> FakeServer()
    {
        var mode = Environment.GetEnvironmentVariable("CODEX_MONITOR_TEST_SERVER_MODE") ?? "normal";
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var method))
            {
                if (mode == "callback")
                {
                    True(root.TryGetProperty("error", out _), "Unexpected credential callback acceptance");
                    await Console.Out.WriteLineAsync("{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":13}}}}");
                }
                continue;
            }
            if (method.GetString() == "initialized") continue;
            var id = root.GetProperty("id").GetInt64();
            if (method.GetString() == "initialize")
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id, result = new { userAgent = "test" } }));
                continue;
            }
            True(method.GetString() == "account/rateLimits/read", "Unexpected request / inference attempted");
            if (mode == "exit") return 0;
            if (mode == "hang") { await Task.Delay(TimeSpan.FromMinutes(1)); continue; }
            if (mode == "malformed") { await Console.Out.WriteLineAsync("malformed-json"); continue; }
            if (mode == "error") { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id, error = new { code = 401, message = "TEST_SECRET https://test.invalid/?access_token=TEST_SECRET" } })); continue; }
            if (mode == "callback") { await Console.Out.WriteLineAsync("{\"id\":99,\"method\":\"account/chatgptAuthTokens/refresh\",\"params\":{}}"); continue; }
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id, result = new { rateLimits = new { primary = new { usedPercent = 13 } } } }));
        }
        return 0;
    }
}

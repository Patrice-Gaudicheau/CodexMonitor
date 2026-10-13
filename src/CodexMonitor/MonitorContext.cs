using System.Diagnostics;
using System.Text.Json;
using CodexMonitor.Core;

namespace CodexMonitor;

internal sealed class MonitorContext : ApplicationContext
{
    private readonly TrayIcon tray = new();
    private readonly Control dispatcher = new();
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 1000 };
    private readonly CancellationTokenSource stopping = new();
    private readonly ToolStripMenuItem refreshItem;
    private readonly ToolStripMenuItem quotaItem;
    private readonly ToolStripMenuItem startupItem;
    private readonly TrayMenu.Items menuItems;
    private Settings settings = Settings.Load();
    private UsageSnapshot? snapshot;
    private AppServerClient? client;
    private bool refreshing, failed, exiting;
    private Exception? readError;
    private string? issueKey;
    private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;
    private string? renderKey;
    private DetailsForm? panel;
    private Task? refreshTask;
    private int successfulReads, manualReads, automaticReads;
    private readonly string? verificationDirectory;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly List<object> observations = [];
    private bool menuVerified, recoveryVerified;
    private bool menuOpened;
    private bool detailsOpened;
    private bool shellRecoveryRequested, shellRecoveryPassed;
    private readonly bool verifyLanguageSwitch;
    private bool languageSwitchVerified, languageSwitchPassed;

    public MonitorContext(string? verificationDirectory, bool verifyLanguageSwitch = false)
    {
        this.verifyLanguageSwitch = verifyLanguageSwitch;
        this.verificationDirectory = verificationDirectory;
        if (verificationDirectory is not null)
        {
            Directory.CreateDirectory(verificationDirectory);
            File.WriteAllText(Path.Combine(verificationDirectory, "session.json"),
                JsonSerializer.Serialize(new { processId = Environment.ProcessId, trayHandle = tray.Handle.ToInt64() }));
        }
        _ = dispatcher.Handle; // Invisible UI dispatcher; no main window.
        menuItems = TrayMenu.Populate(tray.Menu, () => StartRefresh(true), BuildQuotaMenu,
            ToggleStartup, () => MessageBox.Show(AppPresentation.AboutMessage, "CodexMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information), ExitThread, SelectLanguage);
        refreshItem = menuItems.Refresh;
        quotaItem = menuItems.Quota;
        startupItem = menuItems.Startup;
        tray.Menu.Opening += (_, _) =>
        {
            try { startupItem.Checked = StartupRegistration.IsEnabled(); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { startupItem.Checked = false; }
        };
        tray.Click += ShowDetails;
        tray.ShellChanged += () => { renderKey = null; UpdatePresentation(); };
        UpdatePresentation();
        clock.Tick += OnTick;
        clock.Start();
        dispatcher.BeginInvoke(() => StartRefresh(false));
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (exiting) return;
        if (!refreshing && DateTimeOffset.UtcNow >= nextRefresh) StartRefresh(false);
        UpdatePresentation();
        if (verificationDirectory is null) return;
        if (!menuVerified && successfulReads > 0 && elapsed.Elapsed.TotalSeconds >= 3)
        {
            menuVerified = true;
            BuildQuotaMenu();
            tray.Menu.Show(new Point(10, 10));
            tray.Menu.Update();
            var opened = menuOpened = tray.Menu.Visible;
            CaptureWindow(tray.Menu.Bounds, "context-menu.png");
            tray.Menu.Close();
            // Exercises the same click handlers as the actual context menu, without enabling startup.
            var firstQuota = quotaItem.DropDownItems.OfType<ToolStripMenuItem>().Skip(2).FirstOrDefault();
            firstQuota?.PerformClick();
            quotaItem.DropDownItems.OfType<ToolStripMenuItem>().First().PerformClick();
            ShowDetails();
            panel?.Update();
            var detailsVisible = detailsOpened = panel is not null && IsWindowVisible(panel.Handle);
            if (panel is not null) CaptureWindow(panel.Bounds, "details-panel.png");
            panel?.Close();
            observations.Add(new { action = "menu", opened, detailsVisible, quotaItems = quotaItem.DropDownItems.Count, startupEnabled = StartupRegistration.IsEnabled() });
            refreshItem.PerformClick();
        }
        if (!recoveryVerified && manualReads > 0 && successfulReads >= 2 && elapsed.Elapsed.TotalSeconds >= 7 && !refreshing)
        {
            recoveryVerified = true;
            var oldPid = client?.ProcessId;
            client?.Dispose(); client = null;
            failed = true; readError = null; issueKey = nameof(UiText.ReconnectionTest);
            UpdatePresentation();
            observations.Add(new { action = "connectionDisposed", gray = CurrentValue is null, oldPid });
            StartRefresh(true);
        }
        if (!shellRecoveryRequested && successfulReads >= 3 && elapsed.Elapsed.TotalSeconds >= 11)
        {
            shellRecoveryRequested = true;
            tray.VerifyShellRecovery();
        }
        if (shellRecoveryRequested && !shellRecoveryPassed && elapsed.Elapsed.TotalSeconds >= 13)
        {
            shellRecoveryPassed = tray.Registered && tray.GetRectangle() is not null;
            observations.Add(new { action = "taskbarCreatedRecovery", shellRecoveryPassed });
        }
        if (verifyLanguageSwitch && !languageSwitchVerified && shellRecoveryPassed && !refreshing && elapsed.Elapsed.TotalSeconds >= 16)
        {
            languageSwitchVerified = true;
            VerifyLanguageSwitch();
        }
        if (elapsed.Elapsed.TotalSeconds >= 75 && !refreshing)
        {
            WriteVerification();
            ExitThread();
        }
    }

    private void StartRefresh(bool manual)
    {
        if (refreshing || exiting) return;
        refreshing = true;
        refreshItem.Enabled = false;
        if (manual) manualReads++; else automaticReads++;
        refreshTask = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            if (client is null) client = await AppServerClient.ConnectAsync(CodexLocation.Find(), deadline.Token);
            var result = await client.ReadQuotaAsync(deadline.Token);
            var fetched = UsageSnapshot.Parse(result, DateTimeOffset.UtcNow);
            if (exiting) return;
            snapshot = fetched; failed = false; readError = null; issueKey = null;
            successfulReads++;
            if (verificationDirectory is not null) observations.Add(new { action = "liveRead", successfulReads, at = snapshot.FetchedAt, value = CurrentValue, windows = snapshot.Windows.Count, processId = client.ProcessId });
        }
        catch (Exception e)
        {
            if (exiting) return;
            failed = true;
            readError = e; issueKey = null;
            client?.Dispose(); client = null;
        }
        finally
        {
            refreshing = false;
            nextRefresh = DateTimeOffset.UtcNow.AddSeconds(60);
            if (!exiting) { refreshItem.Enabled = true; UpdatePresentation(); }
        }
    }

    private int? CurrentValue => UsagePresentation.DisplayValue(snapshot, settings.SelectedQuota, DateTimeOffset.UtcNow, failed);

    private void UpdatePresentation()
    {
        var now = DateTimeOffset.UtcNow;
        var value = CurrentValue;
        var window = snapshot?.Select(settings.SelectedQuota);
        var tone = value is null ? BadgeTone.Neutral : UsagePresentation.Tone(window?.Remaining);
        var tip = UsagePresentation.Tooltip(snapshot, settings.SelectedQuota, now, failed);
        var size = tray.IconSize;
        var key = $"{value}/{tone}/{size}/{tip}";
        if (key != renderKey || !tray.Registered)
        {
            tray.Set(BadgeRenderer.Create(value, tone, size), tip);
            renderKey = key;
        }
        if (panel is { IsDisposed: false }) panel.UpdateUsage(DetailsViewModel.Create(snapshot, settings.SelectedQuota, now, failed, Problem, refreshing));
    }

    private string? Problem => readError is not null ? AppPresentation.ErrorMessage(readError)
        : issueKey is not null ? Localization.Get(issueKey) : null;

    private void SelectLanguage(LanguagePreference preference)
    {
        var updated = settings with { Language = Localization.PreferenceValue(preference) };
        try
        {
            if (verificationDirectory is null) updated.Save();
            settings = updated;
            Localization.ApplyPreference(preference);
            TrayMenu.Relocalize(menuItems, preference);
            BuildQuotaMenu();
            renderKey = null;
            UpdatePresentation();
            if (verificationDirectory is not null) observations.Add(new { action = "languageSelected",
                preference = Localization.PreferenceValue(preference), culture = Localization.Culture.Name,
                detailsHandle = panel is { IsDisposed: false } ? panel.Handle.ToInt64() : 0,
                tooltip = UsagePresentation.Tooltip(snapshot, settings.SelectedQuota, DateTimeOffset.UtcNow, failed),
                nextRefresh, successfulReads, remaining = CurrentValue });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { MessageBox.Show(UiText.LanguageSaveFailed, "CodexMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void VerifyLanguageSwitch()
    {
        var originalValue = CurrentValue;
        var originalRead = snapshot?.FetchedAt;
        var originalNextRefresh = nextRefresh;
        var originalPid = client?.ProcessId;
        var originalReads = successfulReads;
        var steps = new List<object>();
        ShowDetails();
        var handle = panel!.Handle;
        var originalError = readError;
        var originalIssue = issueKey;
        var originalFailure = failed;
        try
        {
            // The same already-open window must translate even its cached error.
            failed = true; readError = AppServerException.FromCode("401"); issueKey = null;
            foreach (var preference in new[] { LanguagePreference.English, LanguagePreference.French, LanguagePreference.Automatic })
            {
                menuItems.Language.DropDownItems.OfType<ToolStripMenuItem>().Single(i => Equals(i.Tag, preference)).PerformClick();
                panel.Update();
                var surface = panel.Controls.OfType<Panel>().Single(p => p.AutoScroll).Controls.OfType<DetailsSurface>().Single();
                var button = panel.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<Button>()).Single();
                var passed = panel.Handle == handle && panel.Text == UiText.Title && button.Text == UiText.Refresh &&
                    button.AccessibleName == UiText.RefreshAccessible && surface.AccessibilityObject.Name == UiText.DetailsAccessible &&
                    surface.AccessibilityObject.Help?.Contains(Problem!) == true &&
                    menuItems.Refresh.Text == UiText.RefreshNow &&
                    menuItems.Language.DropDownItems.OfType<ToolStripMenuItem>().Count(i => i.Checked) == 1 &&
                    snapshot?.FetchedAt == originalRead && nextRefresh == originalNextRefresh &&
                    client?.ProcessId == originalPid && successfulReads == originalReads;
                CaptureWindow(panel.Bounds, $"switch-{preference}-error.png");
                tray.Menu.Show(new Point(10, 10));
                menuItems.Language.ShowDropDown();
                tray.Menu.Update();
                CaptureWindow(Rectangle.Union(tray.Menu.Bounds, menuItems.Language.DropDown.Bounds), $"switch-{preference}-menu.png");
                tray.Menu.Close();
                steps.Add(new { preference = Localization.PreferenceValue(preference), culture = Localization.Culture.Name,
                    title = panel.Text, tooltip = UsagePresentation.Tooltip(snapshot, settings.SelectedQuota, DateTimeOffset.UtcNow, failed),
                    description = surface.AccessibilityObject.Help, passed });
                if (!passed) throw new InvalidOperationException("Live language switch verification failed.");
            }
            languageSwitchPassed = true;
        }
        finally
        {
            failed = originalFailure; readError = originalError; issueKey = originalIssue;
            Localization.ApplyPreference(LanguagePreference.Automatic);
            TrayMenu.Relocalize(menuItems, Localization.Preference);
            UpdatePresentation();
            panel?.Close();
            observations.Add(new { action = "languageSwitch", languageSwitchPassed, originalValue, originalRead, originalNextRefresh, originalPid, steps });
        }
    }

    private void BuildQuotaMenu()
    {
        TrayMenu.PopulateQuotas(quotaItem, snapshot, settings.SelectedQuota, SelectQuota);
    }

    private void SelectQuota(string? key)
    {
        var updated = settings with { SelectedQuota = key };
        try
        {
            // Verification changes are temporary and do not overwrite the user's preference.
            if (verificationDirectory is null) updated.Save();
            settings = updated; UpdatePresentation();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { MessageBox.Show(UiText.SaveFailed, "CodexMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ToggleStartup()
    {
        try { StartupRegistration.Set(!StartupRegistration.IsEnabled()); startupItem.Checked = StartupRegistration.IsEnabled(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { MessageBox.Show(UiText.StartupFailed, "CodexMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ShowDetails()
    {
        if (panel is { IsDisposed: false }) { panel.Activate(); return; }
        panel = new DetailsForm();
        panel.RefreshRequested += () => StartRefresh(true);
        panel.UpdateUsage(DetailsViewModel.Create(snapshot, settings.SelectedQuota, DateTimeOffset.UtcNow, failed, Problem, refreshing));
        panel.Show();
        ShowWindow(panel.Handle, 5); // Also show correctly if launched via a shortcut with SW_HIDE.
        panel.Activate();
    }

    public void HandleUnexpectedFailure()
    {
        failed = true; readError = null; issueKey = nameof(UiText.UnexpectedError);
        renderKey = null; UpdatePresentation();
    }

    private void WriteVerification()
    {
        Directory.CreateDirectory(verificationDirectory!);
        using var process = Process.GetCurrentProcess();
        var rect = tray.GetRectangle();
        if (rect is { Width: > 0, Height: > 0 }) CaptureWindow(rect.Value, "actual-tray-icon.png");
        using var bitmap = BadgeRenderer.Render(CurrentValue, CurrentValue is null ? BadgeTone.Neutral : UsagePresentation.Tone(snapshot?.Select(settings.SelectedQuota)?.Remaining), tray.IconSize);
        bitmap.Save(Path.Combine(verificationDirectory!, "live-badge.png"));
        File.WriteAllText(Path.Combine(verificationDirectory!, "verification.json"), JsonSerializer.Serialize(new
        {
            version = AppPresentation.Version, uiCulture = Localization.Culture.Name, languageSwitchPassed, successfulReads, manualReads, automaticReads, failed, remaining = CurrentValue,
            windows = snapshot?.Windows.Select(w => new { w.Label, w.BadgeValue, w.DurationMinutes, w.ResetsAt }),
            trayRegistered = tray.Registered, trayRectangle = rect, trayIconPixels = tray.IconSize,
            noMainWindow = process.MainWindowHandle == IntPtr.Zero, memoryMiB = process.WorkingSet64 / 1048576,
            cpuSeconds = process.TotalProcessorTime.TotalSeconds, observations,
            passed = successfulReads >= 4 && automaticReads >= 2 && manualReads >= 2 && !failed && CurrentValue is not null && tray.Registered && rect is not null && menuOpened && detailsOpened && recoveryVerified && shellRecoveryPassed && (!verifyLanguageSwitch || languageSwitchPassed) && process.MainWindowHandle == IntPtr.Zero
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void CaptureWindow(Rectangle bounds, string filename)
    {
        if (verificationDirectory is null || bounds.Width <= 0 || bounds.Height <= 0) return;
        try
        {
            Directory.CreateDirectory(verificationDirectory);
            Application.DoEvents(); // Diagnostic captures only: let the tested windows finish painting.
            DwmFlush();
            Thread.Sleep(150); // Allow the compositor to present this window before copying its pixels.
            using var image = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            image.Save(Path.Combine(verificationDirectory, filename));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or System.Runtime.InteropServices.ExternalException)
        { observations.Add(new { action = "captureUnavailable", filename }); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        clock.Stop(); stopping.Cancel();
        client?.Dispose(); client = null;
        panel?.Close(); tray.Dispose();
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { client?.Dispose(); clock.Dispose(); dispatcher.Dispose(); stopping.Dispose(); }
        base.Dispose(disposing);
    }
}

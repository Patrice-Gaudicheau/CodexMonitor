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
    private readonly ToolStripMenuItem refreshItem = new("Actualiser maintenant");
    private readonly ToolStripMenuItem quotaItem = new("Quota affiché");
    private readonly ToolStripMenuItem startupItem = new("Démarrer avec Windows");
    private Settings settings = Settings.Load();
    private UsageSnapshot? snapshot;
    private AppServerClient? client;
    private bool refreshing, failed, exiting;
    private string? problem;
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

    public MonitorContext(string? verificationDirectory)
    {
        this.verificationDirectory = verificationDirectory;
        _ = dispatcher.Handle; // Invisible UI dispatcher; no main window.
        refreshItem.Click += (_, _) => StartRefresh(true);
        quotaItem.DropDownOpening += (_, _) => BuildQuotaMenu();
        startupItem.Click += (_, _) => ToggleStartup();
        var aboutItem = new ToolStripMenuItem("À propos / Version");
        aboutItem.Click += (_, _) => MessageBox.Show("CodexMonitor 1.0.0\n\nQuotas réels via Codex App Server.\nActualisation toutes les 60 secondes.\nUn clic sur le badge affiche les détails.", "CodexMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        var exitItem = new ToolStripMenuItem("Quitter");
        exitItem.Click += (_, _) => ExitThread();
        tray.Menu.Items.AddRange([refreshItem, quotaItem, startupItem, new ToolStripSeparator(), aboutItem, exitItem]);
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
            failed = true; problem = "Test de reconnexion contrôlé.";
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
            snapshot = fetched; failed = false; problem = null;
            successfulReads++;
            if (verificationDirectory is not null) observations.Add(new { action = "liveRead", successfulReads, at = snapshot.FetchedAt, value = CurrentValue, windows = snapshot.Windows.Count, processId = client.ProcessId });
        }
        catch (Exception e)
        {
            if (exiting) return;
            failed = true;
            problem = e switch
            {
                FileNotFoundException => "Codex CLI introuvable. Vérifiez l’installation native et CODEX_MONITOR_CODEX_PATH.",
                OperationCanceledException => "La lecture Codex a dépassé 25 secondes. Nouvelle tentative automatique dans 60 secondes.",
                AppServerException serverError => serverError.Message,
                JsonException => "Réponse Codex inattendue. Vérifiez la version du CLI ; nouvelle tentative dans 60 secondes.",
                _ => "Connexion Codex indisponible. Vérifiez « codex login status » et le réseau ; nouvelle tentative dans 60 secondes."
            };
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
        if (panel is { IsDisposed: false }) panel.UpdateUsage(DetailsViewModel.Create(snapshot, settings.SelectedQuota, now, failed, problem, refreshing));
    }

    private void BuildQuotaMenu()
    {
        var previous = quotaItem.DropDownItems.Cast<ToolStripItem>().ToArray();
        quotaItem.DropDownItems.Clear();
        foreach (var item in previous) item.Dispose();
        var automatic = new ToolStripMenuItem("Automatique (priorité 5 h)") { Checked = settings.SelectedQuota is null };
        automatic.Click += (_, _) => SelectQuota(null);
        quotaItem.DropDownItems.Add(automatic);
        quotaItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (var window in snapshot?.Windows ?? [])
        {
            var item = new ToolStripMenuItem(window.Label) { Checked = settings.SelectedQuota == window.Key };
            item.Click += (_, _) => SelectQuota(window.Key);
            quotaItem.DropDownItems.Add(item);
        }
        if (snapshot?.Windows.Count is not > 0) quotaItem.DropDownItems.Add(new ToolStripMenuItem("Aucun quota disponible") { Enabled = false });
        else if (settings.SelectedQuota is not null && snapshot.Select(settings.SelectedQuota) is null)
            quotaItem.DropDownItems.Add(new ToolStripMenuItem("Sélection précédente indisponible") { Enabled = false });
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
        { MessageBox.Show("Impossible d’enregistrer le quota sélectionné.", "CodexMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ToggleStartup()
    {
        try { StartupRegistration.Set(!StartupRegistration.IsEnabled()); startupItem.Checked = StartupRegistration.IsEnabled(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { MessageBox.Show("Windows n’a pas autorisé l’enregistrement du démarrage automatique.", "CodexMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ShowDetails()
    {
        if (panel is { IsDisposed: false }) { panel.Activate(); return; }
        panel = new DetailsForm();
        panel.RefreshRequested += () => StartRefresh(true);
        panel.UpdateUsage(DetailsViewModel.Create(snapshot, settings.SelectedQuota, DateTimeOffset.UtcNow, failed, problem, refreshing));
        panel.Show();
        ShowWindow(panel.Handle, 5); // Also show correctly if launched via a shortcut with SW_HIDE.
        panel.Activate();
    }

    public void HandleUnexpectedFailure()
    {
        failed = true; problem = "Une erreur inattendue est survenue. Nouvelle tentative automatique.";
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
            version = "1.0.0", successfulReads, manualReads, automaticReads, failed, remaining = CurrentValue,
            windows = snapshot?.Windows.Select(w => new { w.Label, w.BadgeValue, w.DurationMinutes, w.ResetsAt }),
            trayRegistered = tray.Registered, trayRectangle = rect, trayIconPixels = tray.IconSize,
            noMainWindow = process.MainWindowHandle == IntPtr.Zero, memoryMiB = process.WorkingSet64 / 1048576,
            cpuSeconds = process.TotalProcessorTime.TotalSeconds, observations,
            passed = successfulReads >= 4 && automaticReads >= 2 && manualReads >= 2 && !failed && CurrentValue is not null && tray.Registered && rect is not null && menuOpened && detailsOpened && recoveryVerified && shellRecoveryPassed && process.MainWindowHandle == IntPtr.Zero
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

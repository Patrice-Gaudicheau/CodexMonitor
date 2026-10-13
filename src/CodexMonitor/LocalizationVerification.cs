using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using CodexMonitor.Core;

namespace CodexMonitor;

/// <summary>Opt-in, offline diagnostic. No settings, registry or Windows language changes.</summary>
internal static class LocalizationVerification
{
    public static int VerifyResources()
    {
        var properties = typeof(UiText).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> Read(string culture) => Localization.ResourcesFor(CultureInfo.GetCultureInfo(culture))
            .GetResourceSet(CultureInfo.InvariantCulture, true, false)!.Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        var french = Read("fr-FR");
        var english = Read("en-US");
        Require(properties.SetEquals(french.Keys) && properties.SetEquals(english.Keys), "Resource key sets differ.");
        foreach (var key in properties)
        {
            Require(!string.IsNullOrWhiteSpace(french[key]) && !string.IsNullOrWhiteSpace(english[key]), "Empty resource: " + key);
            string Signature(string value) => string.Join(',', Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}").Select(m => m.Groups[1].Value).Order());
            Require(Signature(french[key]) == Signature(english[key]), "Format arguments differ: " + key);
        }
        var names = typeof(Localization).Assembly.GetManifestResourceNames();
        Require(names.Contains("CodexMonitor.Core.Resources.StringsFrench.resources") &&
            names.Contains("CodexMonitor.Core.Resources.StringsEnglish.resources"), "Resources must be embedded in Core.");
        return properties.Count;
    }

    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var resourceCount = VerifyResources();
        var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        UsageSnapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return UsageSnapshot.Parse(doc.RootElement, now); }
        var snapshot = Parse("""{"rateLimitsByLimitId":{"codex":{"limitName":"Codex","primary":{"usedPercent":13,"windowDurationMins":10080,"resetsAt":1800003600},"secondary":{"usedPercent":12.34,"windowDurationMins":300,"resetsAt":1800007200},"credits":{"balance":"0","unlimited":false}},"other":{"limitName":"External proper name","individualLimit":{"remainingPercent":37},"spendControlReached":true}},"ordinaryUsageAllowed":false}""");
        var normal = Parse("""{"rateLimits":{"primary":{"usedPercent":13,"windowDurationMins":300,"resetsAt":1800003600}}}""");
        var states = new Dictionary<string, DetailsViewModel>
        {
            ["normal"] = DetailsViewModel.Create(normal, null, now, false, null, false),
            ["multiple"] = DetailsViewModel.Create(snapshot, null, now, false, null, false),
            ["missing"] = DetailsViewModel.Create(null, null, now, false, null, false),
            ["stale"] = DetailsViewModel.Create(normal, null, now.AddMinutes(3), false, null, false),
            ["error"] = DetailsViewModel.Create(normal, null, now, true, AppPresentation.ErrorMessage(new IOException()), false),
            ["refreshing"] = DetailsViewModel.Create(normal, null, now, false, null, true),
            ["individual"] = DetailsViewModel.Create(snapshot, "other/individualLimit", now, false, null, false)
        };
        foreach (var remaining in new[] { 100, 50, 9, 0 })
        {
            var valueSnapshot = Parse(JsonSerializer.Serialize(new { rateLimits = new { primary = new { usedPercent = 100 - remaining, windowDurationMins = 300, resetsAt = 1800003600 } } }));
            states.Add($"remaining-{remaining}", DetailsViewModel.Create(valueSnapshot, null, now, false, null, false));
        }
        var observations = new List<object>();
        foreach (var dpi in new[] { 96, 120, 144 })
        foreach (var width in new[] { 520, 440 })
        foreach (var (state, model) in states)
        {
            using var form = new DetailsForm();
            form.UpdateUsage(model);
            form.Show();
            ShowWindow(form.Handle, 5);
            if (form.DeviceDpi != dpi)
            {
                var scale = dpi / (double)form.DeviceDpi;
                var bounds = form.Bounds;
                var suggested = new DpiRect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Left + (int)(bounds.Width * scale), Bottom = bounds.Top + (int)(bounds.Height * scale) };
                SendMessage(form.Handle, 0x2E0, new IntPtr(dpi | (dpi << 16)), ref suggested);
                // Simulate the per-child monitor DPI cache, without altering desktop scaling.
                SetChildDpi(form, dpi);
            }
            Application.DoEvents();
            form.ClientSize = new Size(width * dpi / 96, form.ClientSize.Height);
            form.FitContent(new Rectangle(0, 0, 4000, 2400));
            Application.DoEvents();
            var scroll = form.Controls.OfType<Panel>().Single(p => p.AutoScroll);
            var surface = scroll.Controls.OfType<DetailsSurface>().Single();
            surface.CollectLayoutDiagnostics = true;
            Require(form.DeviceDpi == dpi && surface.DeviceDpi == dpi, "DPI simulation failed.");
            Require(!scroll.VerticalScroll.Visible && !scroll.HorizontalScroll.Visible, "Unnecessary scrollbar.");
            Require(surface.Top >= 0 && surface.Bottom <= scroll.ClientSize.Height, "Clipped content.");
            var button = form.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<Button>()).Single();
            Require(button.Text == (model.Refreshing ? UiText.Refreshing : UiText.Refresh), "Refresh translation.");
            Require(button.Enabled != model.Refreshing && button.AccessibleName == UiText.RefreshAccessible, "Refresh accessibility/state.");
            Require(surface.AccessibilityObject.Name == UiText.DetailsAccessible && surface.AccessibilityObject.Help == model.Description, "Accessible description.");
            var clicks = 0;
            form.RefreshRequested += () => clicks++;
            button.PerformClick();
            Require(clicks == (model.Refreshing ? 0 : 1), "Native refresh action.");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(Path.Combine(directory, $"window-{dpi}-{width}-{state}.png"));
            }
            observations.Add(new { dpi, width, state, formTitle = form.Text, contentHeight = surface.Height,
                viewportHeight = scroll.ClientSize.Height, overflows = surface.TextOverflows.ToArray(), description = model.Description });
            // Keep all failures in the report before reporting them to the caller.
            form.FitContent(new Rectangle(0, 0, 4000, 720));
            Application.DoEvents();
            Require(form.Height <= 688, $"Small-screen bounds: {dpi}/{width}/{state}, height {form.Height}.");
            Require(surface.Height <= scroll.ClientSize.Height || scroll.VerticalScroll.Visible, "Required scrolling missing.");
            scroll.AutoScrollPosition = new Point(0, surface.Height);
            Application.DoEvents();
            Require(surface.Bottom <= scroll.ClientSize.Height, "Last line unreachable.");
            form.Close();
        }
        using var menu = new ContextMenuStrip();
        var refreshes = 0;
        string? selected = "unchanged";
        var items = TrayMenu.Populate(menu, () => refreshes++, () => { }, () => { }, () => { }, () => { });
        TrayMenu.PopulateQuotas(items.Quota, snapshot, null, key => selected = key);
        items.Refresh.PerformClick();
        items.Quota.DropDownItems.OfType<ToolStripMenuItem>().Skip(1).First().PerformClick();
        Require(refreshes == 1 && selected == snapshot.Windows[0].Key, "Context menu handlers/identity.");
        menu.Show(new Point(10, 10));
        menu.Update();
        Application.DoEvents();
        using (var image = new Bitmap(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(image, menu.ClientRectangle);
            image.Save(Path.Combine(directory, "context-menu.png"));
        }
        menu.Close();
        var tooltip = UsagePresentation.Tooltip(snapshot, null, now, false);
        Require(tooltip.Length <= 127 && !char.IsHighSurrogate(tooltip[^1]), "Invalid tooltip length/Unicode.");
        var aboutDialog = VerifyAboutDialog(directory);
        File.WriteAllText(Path.Combine(directory, "localization.json"), JsonSerializer.Serialize(new
        {
            version = AppPresentation.Version, culture = Localization.Culture.Name, resourceCount,
            embeddedResources = typeof(Localization).Assembly.GetManifestResourceNames(),
            menu = menu.Items.OfType<ToolStripMenuItem>().Select(i => i.Text).ToArray(), tooltip,
            about = AppPresentation.AboutMessage, aboutDialog, observations
        }, new JsonSerializerOptions { WriteIndented = true }));
        // Fail after saving all geometry and screenshots to make any clipping reviewable.
        foreach (var path in Directory.EnumerateFiles(directory, "localization.json"))
        {
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var observation in report.RootElement.GetProperty("observations").EnumerateArray())
                Require(observation.GetProperty("overflows").GetArrayLength() == 0, "Text overflow: " + observation.GetRawText());
        }
    }

    private static string VerifyAboutDialog(string directory)
    {
        string? captured = null;
        Exception? failure = null;
        var thread = GetCurrentThreadId();
        using var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            EnumThreadWindows(thread, (window, _) =>
            {
                var className = new StringBuilder(128);
                GetClassName(window, className, className.Capacity);
                if (className.ToString() != "#32770") return true;
                timer.Stop();
                try
                {
                    var texts = new List<string>();
                    EnumChildWindows(window, (child, _) =>
                    {
                        var text = new StringBuilder(4096);
                        GetWindowText(child, text, text.Capacity);
                        if (text.Length > 0) texts.Add(text.ToString());
                        return true;
                    }, IntPtr.Zero);
                    captured = string.Join('\n', texts).Replace("\r", "");
                    GetWindowRect(window, out var rect);
                    using var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
                    using var graphics = Graphics.FromImage(bitmap);
                    var dc = graphics.GetHdc();
                    try { Require(PrintWindow(window, dc, 2), "Native dialog rendering failed."); }
                    finally { graphics.ReleaseHdc(dc); }
                    bitmap.Save(Path.Combine(directory, "about-dialog.png"));
                }
                catch (Exception error) { failure = error; }
                finally { EndDialog(window, new IntPtr(1)); } // IDOK, own modal diagnostic dialog only.
                return true;
            }, IntPtr.Zero);
        };
        timer.Start();
        MessageBox.Show(AppPresentation.AboutMessage, "CodexMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (failure is not null) throw new InvalidOperationException("About dialog capture failed.", failure);
        Require(captured?.Contains(AppPresentation.AboutMessage.Replace("\r", "")) == true, "Native about text differs.");
        return captured!;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void SetChildDpi(Control control, int dpi)
    {
        var property = typeof(Control).GetProperty("DeviceDpiInternal", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DPI fixture needs updating for this WinForms runtime.");
        foreach (Control child in control.Controls) { property.SetValue(child, dpi); SetChildDpi(child, dpi); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DpiRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, ref DpiRect bounds);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out DpiRect rect);
    [DllImport("user32.dll")] private static extern bool EndDialog(IntPtr window, IntPtr result);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}

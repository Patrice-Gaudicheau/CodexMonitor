using System.Globalization;
using System.Text.Json;
using CodexMonitor;
using CodexMonitor.Core;

internal static class LocalizationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
    public static void Run(Action<string, Action> test)
    {
        test("UI language uses French variants and English fallback, independent of region", () =>
        {
            foreach (var culture in new[] { "fr-FR", "fr-CA", "fr-BE", "fr-CH", "fr", "en-US", "en-GB", "de-DE", "es-ES", "ja-JP", "" })
            {
                using var scope = new CultureScope(culture);
                var french = culture.StartsWith("fr", StringComparison.Ordinal);
                Equal(french ? "fr-FR" : "en-US", Localization.Culture.Name);
                Equal(french ? "Quotas Codex" : "Codex quotas", UiText.Title);
                Equal("es-ES", CultureInfo.CurrentCulture.Name);
            }
            var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
            try
            {
                using var scope = new CultureScope("fr-CA");
                Localization.Initialize();
                Equal("fr-FR", CultureInfo.CurrentUICulture.Name);
                Equal("fr-FR", CultureInfo.DefaultThreadCurrentUICulture?.Name);
                var workerCulture = "";
                var thread = new Thread(() => workerCulture = Localization.Culture.Name);
                thread.Start(); thread.Join();
                Equal("fr-FR", workerCulture);
            }
            finally { CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
        });
        test("French and English embedded resource sets are complete with matching format arguments", () =>
        {
            True(LocalizationVerification.VerifyResources() > 80, "Resource inventory unexpectedly incomplete");
            foreach (var culture in new[] { "fr-FR", "en-US" })
            {
                using var scope = new CultureScope(culture);
                foreach (var property in typeof(UiText).GetProperties())
                    True(property.GetValue(null) is string { Length: > 0 }, "Missing " + property.Name);
                True(!AppPresentation.AboutMessage.Contains("{0}"), "About format argument not replaced");
                True(AppPresentation.AboutMessage.StartsWith("CodexMonitor 1.1.0"), "Wrong about version");
            }
        });
        test("localized dates, times and fractions keep local timezone rather than regional culture", () =>
        {
            foreach (var culture in new[] { "fr-FR", "en-US", "de-DE" })
            {
                using var scope = new CultureScope(culture);
                var snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":13.1,"windowDurationMins":300,"resetsAt":1800003600},"secondary":{"usedPercent":12.34,"windowDurationMins":10080,"resetsAt":1800007200}}}""");
                var model = DetailsViewModel.Create(snapshot, null, Now, false, null, false);
                var french = culture == "fr-FR";
                var expectedCulture = CultureInfo.GetCultureInfo(french ? "fr-FR" : "en-US");
                var localReset = DateTimeOffset.FromUnixTimeSeconds(1800003600).ToLocalTime();
                Equal(localReset.ToString(french ? "dd MMMM yyyy" : "MMMM d, yyyy", expectedCulture), model.ResetDate);
                Equal(localReset.ToString(french ? "HH:mm '· heure locale'" : "h:mm tt '· local time'", expectedCulture), model.ResetTime);
                Equal(Now.ToLocalTime().ToString(french ? "HH:mm:ss '· heure locale'" : "h:mm:ss tt '· local time'", expectedCulture), model.ReadTime);
                Equal(french ? "87,66 % restants" : "87.66% remaining", model.OtherQuotas[0].Value);
                Equal(french ? "86 %" : "86%", model.PercentageText);
                True(UsagePresentation.Tooltip(snapshot, null, Now, false).Contains(french ? "86,9%" : "86.9%"), "Tooltip number culture");
            }
        });
        test("localized missing, disappeared, stale, reset and failed quota data", () =>
        {
            foreach (var culture in new[] { "fr-FR", "en-US" })
            {
                using var scope = new CultureScope(culture);
                var snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":13,"windowDurationMins":10080,"resetsAt":1800003600}}}""");
                var missing = DetailsViewModel.Create(null, null, Now, false, null, false);
                Equal(UiText.MissingPercentage, missing.Issue); Equal(UiText.DataUnavailable, missing.Status);
                Equal(UiText.NoReading, missing.ReadDate); Equal(UiText.NotReported, missing.ResetDate);
                Equal(UiText.MissingSelected, DetailsViewModel.Create(snapshot, "missing/primary", Now, false, null, false).Issue);
                foreach (var when in new[] { Now.AddMinutes(3), Now.AddHours(1) })
                {
                    var stale = DetailsViewModel.Create(snapshot, null, when, false, null, false);
                    Equal<int?>(null, stale.Percentage); Equal(UiText.Stale, stale.Issue);
                    Equal(UsagePresentation.Tooltip(null, null, when, false), "CodexMonitor\n" + UiText.QuotasUnavailable);
                }
                var failed = DetailsViewModel.Create(snapshot, null, Now, true, UiText.ConnectionUnavailable, false);
                Equal<int?>(null, failed.Percentage); Equal(UiText.ConnectionUnavailable, failed.Issue);
                True(failed.ReadDate != UiText.NoReading, "Last successful read lost");
                True(!UsagePresentation.Tooltip(snapshot, null, Now, true).Contains("87%"), "Stale percentage in tooltip");
            }
        });
        test("Core notes and labels localize without changing identifiers or supplied proper names", () =>
        {
            foreach (var culture in new[] { "fr-FR", "en-US" })
            {
                using var scope = new CultureScope(culture);
                var snapshot = Parse("""{"rateLimitsByLimitId":{"custom":{"limitName":"Proper Name","primary":{"usedPercent":13,"windowDurationMins":10080},"individualLimit":{"remainingPercent":37},"credits":{"unlimited":true},"spendControlReached":true,"rateLimitReachedType":"protocol_value"}},"ordinaryUsageAllowed":false}""");
                Equal("custom/primary", snapshot.Windows[0].Key); Equal("Proper Name", snapshot.Windows[0].BucketName);
                Equal(UiText.Weekly, snapshot.Windows[0].DurationLabel); Equal(UiText.IndividualBudget, snapshot.Windows[1].DurationLabel);
                True(snapshot.Notes.Contains(UiText.IncludedBlocked), "Core blocked warning");
                True(snapshot.Notes.Any(n => n.Contains(UiText.Unlimited)), "Core credit translation");
                True(snapshot.Notes.Any(n => n.Contains("protocol_value")), "Protocol value translated");
            }
        });
        test("translated connection, authentication, timeout, malformed response and settings warnings", () =>
        {
            foreach (var culture in new[] { "fr-FR", "en-US" })
            {
                using var scope = new CultureScope(culture);
                Equal(UiText.CliMissing, AppPresentation.ErrorMessage(new FileNotFoundException()));
                Equal(UiText.Timeout, AppPresentation.ErrorMessage(new OperationCanceledException()));
                Equal(UiText.InvalidResponse, AppPresentation.ErrorMessage(new JsonException("TEST_SECRET")));
                Equal(UiText.ConnectionUnavailable, AppPresentation.ErrorMessage(new IOException("TEST_SECRET")));
                var serverError = new AppServerException(Localization.Format(nameof(UiText.ServerRefused), 401));
                Equal(serverError.Message, AppPresentation.ErrorMessage(serverError));
                True(serverError.Message.Contains("codex login status"), "Auth instructions lost");
                True(culture == "fr-FR" ? UiText.SaveFailed.StartsWith("Impossible") : UiText.SaveFailed.StartsWith("Unable"), "Save warning");
                True(culture == "fr-FR" ? UiText.StartupFailed.StartsWith("Windows n’a") : UiText.StartupFailed.StartsWith("Windows did"), "Startup warning");
            }
        });
        test("localized actual context menu, quota submenu, tooltip and callbacks", () =>
        {
            foreach (var culture in new[] { "fr-FR", "en-US" })
            {
                using var scope = new CultureScope(culture);
                using var menu = new System.Windows.Forms.ContextMenuStrip();
                var clicks = new int[4];
                var items = TrayMenu.Populate(menu, () => clicks[0]++, () => { }, () => clicks[1]++, () => clicks[2]++, () => clicks[3]++);
                Equal(culture == "fr-FR" ? "Actualiser maintenant" : "Refresh now", items.Refresh.Text);
                Equal(UiText.DisplayedQuota, items.Quota.Text); Equal(UiText.Startup, items.Startup.Text);
                var commands = menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().ToArray();
                Equal(UiText.About, items.About.Text); Equal(UiText.Exit, items.Exit.Text);
                foreach (var item in new[] { items.Refresh, items.Startup, items.About, items.Exit }) item.PerformClick();
                True(clicks.All(c => c == 1), "Menu click handler changed");
                TrayMenu.PopulateQuotas(items.Quota, null, null, _ => { });
                Equal(UiText.Automatic, items.Quota.DropDownItems[0].Text);
                Equal(UiText.NoQuotas, items.Quota.DropDownItems[2].Text);
                True(UsagePresentation.Tooltip(null, null, Now, true).Contains(UiText.ReadingUnavailable), "Failure tooltip not localized");
            }
        });
        foreach (var culture in new[] { "fr-FR", "en-US" })
            test($"actual Windows forms in {culture} at 100/125/150% DPI, narrow layout and all data states", () =>
            {
                using var scope = new CultureScope(culture);
                LocalizationVerification.Run(Path.Combine(Environment.CurrentDirectory, "artifacts", "verification", "localization", culture));
            });
    }
    internal sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previousUi = CultureInfo.CurrentUICulture;
        private readonly CultureInfo previousRegion = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousWindows = Localization.WindowsUiCulture;
        private readonly LanguagePreference previousPreference = Localization.Preference;
        private readonly CultureInfo? previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        public CultureScope(string culture)
        {
            Localization.Initialize(CultureInfo.GetCultureInfo(culture));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
        }
        public void Dispose()
        {
            Localization.Initialize(previousWindows, previousPreference);
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previousRegion;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
        }
    }
    private static UsageSnapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return UsageSnapshot.Parse(doc.RootElement, Now); }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, actual {actual}"); }
}

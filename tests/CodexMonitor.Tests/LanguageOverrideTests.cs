using System.Globalization;
using System.Text.Json;
using CodexMonitor;
using CodexMonitor.Core;

internal static class LanguageOverrideTests
{
    public static void Run(Action<string, Action> test)
    {
        test("manual override and return to original Windows language", () =>
        {
            foreach (var windows in new[] { "fr-FR", "fr-CA", "en-US", "de-DE", "ja-JP" })
            {
                using var scope = new LocalizationTests.CultureScope(windows);
                var automatic = windows.StartsWith("fr", StringComparison.Ordinal) ? "fr-FR" : "en-US";
                Equal(automatic, Localization.Culture.Name);
                foreach (var preference in new[] { LanguagePreference.English, LanguagePreference.French, LanguagePreference.Automatic })
                {
                    Localization.ApplyPreference(preference);
                    Equal(preference == LanguagePreference.Automatic ? automatic : preference == LanguagePreference.French ? "fr-FR" : "en-US", Localization.Culture.Name);
                    Equal(windows, Localization.WindowsUiCulture.Name);
                    Equal(preference, Localization.Preference);
                    Equal("es-ES", CultureInfo.CurrentCulture.Name);
                }
            }
        });
        test("old settings and persisted language survive disk round trips without losing quota identity", () =>
        {
            var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "verification", "language-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            try
            {
                File.WriteAllText(path, """{"SelectedQuota":"custom/secondary"}""");
                var old = Settings.Load(path);
                Equal("custom/secondary", old.SelectedQuota);
                Equal(LanguagePreference.Automatic, old.PreferredLanguage);
                foreach (var preference in Enum.GetValues<LanguagePreference>())
                {
                    var settings = old with { Language = Localization.PreferenceValue(preference) };
                    settings.Save(path);
                    var loaded = Settings.Load(path);
                    Equal(settings, loaded);
                    Equal(preference, loaded.PreferredLanguage);
                    Equal("custom/secondary", loaded.SelectedQuota);
                    True(!File.Exists(path + ".tmp"), "Atomic settings write left a temporary file");
                    using var scope = new LocalizationTests.CultureScope("de-DE");
                    Localization.Initialize(CultureInfo.GetCultureInfo("de-DE"), loaded.PreferredLanguage);
                    Equal(preference == LanguagePreference.French ? "fr-FR" : "en-US", Localization.Culture.Name);
                }
                foreach (var language in new[] { "null", "\"unknown-future-value\"" })
                {
                    File.WriteAllText(path, "{\"SelectedQuota\":\"custom/secondary\",\"Language\":" + language + "}");
                    var settings = Settings.Load(path);
                    Equal(LanguagePreference.Automatic, settings.PreferredLanguage);
                    Equal("custom/secondary", settings.SelectedQuota);
                }
            }
            finally { File.Delete(path); File.Delete(path + ".tmp"); Directory.Delete(directory); }
        });
        test("live switch relocalizes cached quota notes, errors and an existing native details window", () =>
        {
            using var scope = new LocalizationTests.CultureScope("fr-CA");
            var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
            using var document = JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":13,"windowDurationMins":10080,"resetsAt":1800003600},"credits":{"unlimited":true},"spendControlReached":true},"ordinaryUsageAllowed":false}""");
            var snapshot = UsageSnapshot.Parse(document.RootElement, now);
            var serverError = AppServerException.FromCode("401");
            var unknownError = AppServerException.FromCode(null);
            using var form = new DetailsForm();
            using var menu = new System.Windows.Forms.ContextMenuStrip();
            TrayMenu.Items? items = null;
            void Select(LanguagePreference preference)
            {
                Localization.ApplyPreference(preference);
                TrayMenu.Relocalize(items!, preference);
                TrayMenu.PopulateQuotas(items!.Quota, snapshot, null, _ => { });
                form.UpdateUsage(DetailsViewModel.Create(snapshot, null, now, true, AppPresentation.ErrorMessage(serverError), true));
            }
            items = TrayMenu.Populate(menu, () => { }, () => { }, () => { }, () => { }, () => { }, Select);
            Select(LanguagePreference.Automatic);
            form.Show();
            var handle = form.Handle;
            var button = form.Controls.OfType<System.Windows.Forms.Panel>().SelectMany(p => p.Controls.OfType<System.Windows.Forms.Button>()).Single();
            foreach (var preference in new[] { LanguagePreference.English, LanguagePreference.French, LanguagePreference.Automatic })
            {
                items.Language.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i => Equals(i.Tag, preference)).PerformClick();
                System.Windows.Forms.Application.DoEvents();
                var english = preference == LanguagePreference.English;
                Equal(english ? "Codex quotas" : "Quotas Codex", form.Text);
                Equal(handle, form.Handle);
                Equal(UiText.Refreshing, button.Text); Equal(UiText.RefreshAccessible, button.AccessibleName);
                True(!button.Enabled, "Switch enabled busy refresh");
                Equal(UiText.RefreshNow, items.Refresh.Text);
                Equal(UiText.Weekly, snapshot.Windows[0].DurationLabel);
                True(snapshot.Notes.Contains(UiText.IncludedBlocked), "Cached included-usage note retained previous language");
                True(snapshot.Notes.Any(n => n.Contains(UiText.Unlimited)), "Cached credits retained previous language");
                var surface = form.Controls.OfType<System.Windows.Forms.Panel>().Single(p => p.AutoScroll).Controls.OfType<DetailsSurface>().Single();
                Equal(UiText.DetailsAccessible, surface.AccessibilityObject.Name);
                True(surface.AccessibilityObject.Help!.Contains(serverError.Message), "Cached authentication error retained previous language");
                True(serverError.Message.StartsWith(english ? "Codex rejected" : "Codex refuse"), "Authentication error language");
                True(unknownError.Message.Contains(UiText.UnknownBalance), "Unknown error code retained previous language");
                True(UsagePresentation.Tooltip(snapshot, null, now, true).Contains(UiText.ReadingUnavailable), "Tooltip retained previous language");
                Equal(1, items.Language.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Count(i => i.Checked));
                Equal(87, snapshot.Windows[0].BadgeValue);
                Equal(now, snapshot.FetchedAt);
            }
            form.Close();
        });
        test("an in-flight async continuation cannot restore stale-language resources", () =>
        {
            using var scope = new LocalizationTests.CultureScope("fr-FR");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = Task.Run(async () =>
            {
                await release.Task.ConfigureAwait(false);
                return (CultureInfo.CurrentUICulture.Name, UiText.Title, AppServerException.FromCode("401").Message);
            });
            Localization.ApplyPreference(LanguagePreference.English);
            release.SetResult();
            var result = worker.GetAwaiter().GetResult();
            Equal("fr-FR", result.Item1); // Deliberately captured old ExecutionContext.
            Equal("Codex quotas", result.Title);
            True(result.Message.StartsWith("Codex rejected"), "Async error uses stale culture");
        });
    }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, actual {actual}"); }
}

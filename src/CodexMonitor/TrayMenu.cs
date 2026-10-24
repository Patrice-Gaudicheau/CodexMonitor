using CodexMonitor.Core;

namespace CodexMonitor;

internal static class TrayMenu
{
    public sealed record Items(ToolStripMenuItem Refresh, ToolStripMenuItem Quota, ToolStripMenuItem Startup,
        ToolStripMenuItem Language, ToolStripMenuItem About, ToolStripMenuItem Exit, ToolStripMenuItem WslFallback);

    public static Items Populate(
        ContextMenuStrip menu, Action refresh, Action buildQuotas, Action startup, Action about, Action exit,
        Action<LanguagePreference>? selectLanguage = null, Action? toggleWslFallback = null, bool allowWslFallback = true)
    {
        var refreshItem = new ToolStripMenuItem(UiText.RefreshNow);
        var quotaItem = new ToolStripMenuItem(UiText.DisplayedQuota);
        var startupItem = new ToolStripMenuItem(UiText.Startup);
        var aboutItem = new ToolStripMenuItem(UiText.About);
        var exitItem = new ToolStripMenuItem(UiText.Exit);
        var languageItem = new ToolStripMenuItem(UiText.LanguageMenu);
        var wslItem = new ToolStripMenuItem(UiText.WslFallback) { Checked = allowWslFallback };
        wslItem.Click += (_, _) => toggleWslFallback?.Invoke();
        foreach (var preference in Enum.GetValues<LanguagePreference>())
        {
            var item = new ToolStripMenuItem { Tag = preference };
            item.Click += (_, _) => selectLanguage?.Invoke(preference);
            languageItem.DropDownItems.Add(item);
        }
        refreshItem.Click += (_, _) => refresh();
        quotaItem.DropDownOpening += (_, _) => buildQuotas();
        startupItem.Click += (_, _) => startup();
        aboutItem.Click += (_, _) => about();
        exitItem.Click += (_, _) => exit();
        menu.Items.AddRange([refreshItem, quotaItem, wslItem, startupItem, languageItem, new ToolStripSeparator(), aboutItem, exitItem]);
        var items = new Items(refreshItem, quotaItem, startupItem, languageItem, aboutItem, exitItem, wslItem);
        Relocalize(items, Localization.Preference);
        return items;
    }

    public static void Relocalize(Items items, LanguagePreference selected)
    {
        items.Refresh.Text = UiText.RefreshNow;
        items.Quota.Text = UiText.DisplayedQuota;
        items.Startup.Text = UiText.Startup;
        items.Language.Text = UiText.LanguageMenu;
        items.WslFallback.Text = UiText.WslFallback;
        items.About.Text = UiText.About;
        items.Exit.Text = UiText.Exit;
        foreach (var item in items.Language.DropDownItems.OfType<ToolStripMenuItem>())
        {
            var preference = (LanguagePreference)item.Tag!;
            item.Checked = preference == selected;
            item.Text = preference switch
            {
                LanguagePreference.French => UiText.LanguageFrench,
                LanguagePreference.English => UiText.LanguageEnglish,
                _ => UiText.LanguageAutomatic
            };
        }
    }

    public static void PopulateQuotas(ToolStripMenuItem quotaItem, UsageSnapshot? snapshot, string? selected, Action<string?> select)
    {
        var previous = quotaItem.DropDownItems.Cast<ToolStripItem>().ToArray();
        quotaItem.DropDownItems.Clear();
        foreach (var item in previous) item.Dispose();
        var automatic = new ToolStripMenuItem(UiText.Automatic) { Checked = selected is null };
        automatic.Click += (_, _) => select(null);
        quotaItem.DropDownItems.Add(automatic);
        quotaItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (var window in snapshot?.Windows ?? [])
        {
            var item = new ToolStripMenuItem(window.Label) { Checked = selected == window.Key };
            item.Click += (_, _) => select(window.Key);
            quotaItem.DropDownItems.Add(item);
        }
        if (snapshot?.Windows.Count is not > 0)
            quotaItem.DropDownItems.Add(new ToolStripMenuItem(UiText.NoQuotas) { Enabled = false });
        else if (selected is not null && snapshot.Select(selected) is null)
            quotaItem.DropDownItems.Add(new ToolStripMenuItem(UiText.PreviousUnavailable) { Enabled = false });
    }
}

using System.Text.Json;

namespace CodexMonitor.Core;

public sealed record QuotaWindow(string Key, string BucketId, string BucketName, string Slot,
    double? UsedPercent, long? DurationMinutes, DateTimeOffset? ResetsAt, bool DirectRemaining = false)
{
    public double? Remaining => UsedPercent is { } used && double.IsFinite(used)
        ? Math.Clamp(DirectRemaining ? used : 100 - used, 0, 100) : null;
    // Flooring avoids promising more quota than remains when a future server returns fractions.
    public int? BadgeValue => Remaining is { } value ? (int)Math.Floor(value) : null;
    public string DurationLabel => DurationMinutes switch
    {
        300 => Localization.Format(nameof(UiText.Hours), 5), 10080 => UiText.Weekly,
        > 0 when DurationMinutes % 1440 == 0 => Localization.Format(nameof(UiText.Days), DurationMinutes / 1440),
        > 0 when DurationMinutes % 60 == 0 => Localization.Format(nameof(UiText.Hours), DurationMinutes / 60), > 0 => Localization.Format(nameof(UiText.Minutes), DurationMinutes),
        _ => Slot == "individualLimit" ? UiText.IndividualBudget : Slot == "primary" ? UiText.Primary : UiText.Secondary
    };
    public string Label => $"{BucketName} · {DurationLabel}";
    public bool IsFresh(DateTimeOffset fetchedAt, DateTimeOffset now) =>
        now >= fetchedAt && now - fetchedAt < UsageSnapshot.MaximumAge && (ResetsAt is null || now < ResetsAt);
    public string ResetLabel => ResetsAt is { } reset ? Localization.Date(reset, UiText.TooltipDatePattern) : UiText.Unknown;
}

public enum QuotaNoteKind { IncludedBlocked, Credits, LimitReached, SpendReached }

/// <summary>Store facts rather than translated sentences so cached data can change language.</summary>
public sealed record QuotaNote(QuotaNoteKind Kind, string? Bucket = null, string? Value = null, bool Unlimited = false)
{
    public string Text => Kind switch
    {
        QuotaNoteKind.IncludedBlocked => UiText.IncludedBlocked,
        QuotaNoteKind.Credits => Localization.Format(nameof(UiText.Credits), Bucket, Unlimited ? UiText.Unlimited : Value ?? UiText.UnknownBalance),
        QuotaNoteKind.SpendReached => Localization.Format(nameof(UiText.SpendReached), Bucket),
        _ => Localization.Format(nameof(UiText.ReachedNotice), Bucket, Value)
    };
}

public sealed record UsageSnapshot(IReadOnlyList<QuotaWindow> Windows, IReadOnlyList<QuotaNote> NoteItems, DateTimeOffset FetchedAt)
{
    public IReadOnlyList<string> Notes => NoteItems.Select(note => note.Text).ToArray();
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(3);

    public QuotaWindow? Select(string? preference)
    {
        if (!string.IsNullOrEmpty(preference))
            return Windows.FirstOrDefault(w => w.Key == preference); // A disappeared selection must not silently change identity.
        return Windows.OrderBy(w => w.BucketId == "codex" ? 0 : 1)
            .ThenBy(w => w.DurationMinutes == 300 ? 0 : w.DurationMinutes == 10080 ? 1 : 2)
            .ThenBy(w => w.Slot == "primary" ? 0 : 1).FirstOrDefault(w => w.Remaining is not null)
            ?? Windows.FirstOrDefault();
    }

    public static UsageSnapshot Parse(JsonElement result, DateTimeOffset now)
    {
        if (result.ValueKind != JsonValueKind.Object) throw new JsonException(UiText.ExpectedQuota);
        var windows = new List<QuotaWindow>();
        var notes = new List<QuotaNote>();
        var bucketIds = new HashSet<string>(StringComparer.Ordinal);
        // A present map is authoritative, including an empty map. Legacy is used only when the map is unavailable.
        if (result.TryGetProperty("rateLimitsByLimitId", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var bucket in map.EnumerateObject()) AddBucket(bucket.Name, bucket.Value);
        }
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            AddBucket(String(legacy, "limitId") ?? "codex", legacy);
        if (result.TryGetProperty("ordinaryUsageAllowed", out var allowed) && allowed.ValueKind == JsonValueKind.False)
            notes.Add(new(QuotaNoteKind.IncludedBlocked));
        return new(windows, notes, now);

        void AddBucket(string id, JsonElement bucket)
        {
            if (bucket.ValueKind != JsonValueKind.Object || !bucketIds.Add(id)) return;
            var name = CleanLabel(String(bucket, "limitName") ?? id);
            foreach (var slot in new[] { "primary", "secondary", "individualLimit" })
            {
                if (!bucket.TryGetProperty(slot, out var window) || window.ValueKind != JsonValueKind.Object) continue;
                var direct = slot == "individualLimit";
                windows.Add(new($"{id}/{slot}", id, name, slot,
                    Number(window, direct ? "remainingPercent" : "usedPercent"), Integer(window, "windowDurationMins"), Timestamp(window), direct));
            }
            if (bucket.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
            {
                var unlimited = credits.TryGetProperty("unlimited", out var u) && u.ValueKind == JsonValueKind.True;
                var balance = String(credits, "balance") is { } supplied ? CleanLabel(supplied) : null;
                notes.Add(new(QuotaNoteKind.Credits, name, balance, unlimited));
            }
            if (String(bucket, "rateLimitReachedType") is { } reached) notes.Add(new(QuotaNoteKind.LimitReached, name, CleanLabel(reached)));
            if (bucket.TryGetProperty("spendControlReached", out var spend) && spend.ValueKind == JsonValueKind.True)
                notes.Add(new(QuotaNoteKind.SpendReached, name));
        }
    }

    private static string? String(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Number(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
    private static long? Integer(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.TryGetInt64Safe(out var n) ? n : null;
    private static DateTimeOffset? Timestamp(JsonElement e)
    {
        var seconds = Integer(e, "resetsAt");
        if (seconds is null) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds.Value); } catch (ArgumentOutOfRangeException) { return null; }
    }
    private static string CleanLabel(string value) => new(value.Where(c => !char.IsControl(c)).Take(80).ToArray());
}

internal static class JsonExtensions
{
    public static bool TryGetInt64Safe(this JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }
}

public enum BadgeTone { Neutral, Green, Orange, Red }
public static class UsagePresentation
{
    public static BadgeTone Tone(double? remaining) => remaining switch
    {
        > 50 => BadgeTone.Green, >= 20 => BadgeTone.Orange, >= 0 => BadgeTone.Red, _ => BadgeTone.Neutral
    };
    public static int? DisplayValue(UsageSnapshot? snapshot, string? selected, DateTimeOffset now, bool failed) =>
        !failed && snapshot?.Select(selected) is { } window && window.IsFresh(snapshot.FetchedAt, now) ? window.BadgeValue : null;
    public static string Tooltip(UsageSnapshot? snapshot, string? selected, DateTimeOffset now, bool failed)
    {
        var lines = new List<string> { "CodexMonitor" };
        if (failed) lines.Add(UiText.ReadingUnavailable);
        if (snapshot is null) lines.Add(UiText.QuotasUnavailable);
        else
        {
            var chosen = snapshot.Select(selected);
            var ordered = snapshot.Windows.OrderBy(w => w == chosen ? 0 : 1).ToList();
            foreach (var w in ordered)
            {
                var value = !failed && w.IsFresh(snapshot.FetchedAt, now) && w.Remaining is { } remaining
                    ? Localization.Format(nameof(UiText.TooltipPercent), remaining) : UiText.UnavailableLower;
                lines.Add(Localization.Format(nameof(UiText.TooltipLine), w.Label, value, w.ResetLabel));
            }
            if (ordered.Count == 0 || chosen is null) lines.Add(UiText.SelectedUnavailable);
        }
        var text = string.Join('\n', lines);
        if (text.Length <= 127) return text;
        var suffix = UiText.TooltipMore;
        var shortened = text[..(127 - suffix.Length)];
        if (char.IsHighSurrogate(shortened[^1])) shortened = shortened[..^1];
        return shortened + suffix;
    }
}

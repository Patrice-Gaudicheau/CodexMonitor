using CodexMonitor.Core;

namespace CodexMonitor;

/// <summary>Presentation only. Selection, quota math and freshness use the existing core rules.</summary>
public sealed record DetailQuotaRow(string Label, string Value, string Reset);

public sealed record DetailsViewModel(
    string QuotaLabel, string Bucket, int? Percentage, double? Remaining, BadgeTone Tone,
    string Status, string? Issue, string ResetDate, string ResetTime, string ReadDate, string ReadTime,
    bool Refreshing, IReadOnlyList<DetailQuotaRow> OtherQuotas, IReadOnlyList<string> Notes)
{
    public static string Cadence => UiText.Cadence;
    public static string Freshness => UiText.Freshness;
    public string PercentageText => Percentage is { } value ? Localization.Format(nameof(UiText.Percent), value) : "—";
    public string Description => string.Join("\n", new[]
    {
        Localization.Format(nameof(UiText.DescriptionQuota), QuotaLabel, Bucket, PercentageText, Status), Issue,
        Localization.Format(nameof(UiText.DescriptionReset), ResetDate, ResetTime),
        Localization.Format(nameof(UiText.DescriptionRead), ReadDate, ReadTime),
        Cadence, Freshness,
        string.Join("\n", OtherQuotas.Select(q => Localization.Format(nameof(UiText.DescriptionOther), q.Label, q.Value, q.Reset))),
        string.Join("\n", Notes)
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static DetailsViewModel Create(UsageSnapshot? snapshot, string? selected, DateTimeOffset now,
        bool failed, string? problem, bool refreshing)
    {
        var window = snapshot?.Select(selected);
        var value = UsagePresentation.DisplayValue(snapshot, selected, now, failed);
        var remaining = value is null ? null : window?.Remaining;
        var tone = UsagePresentation.Tone(remaining);
        var status = value is null ? UiText.DataUnavailable : value == 0 ? UiText.Exhausted : tone switch
        {
            BadgeTone.Green => UiText.Available, BadgeTone.Orange => UiText.Watch, _ => UiText.Low
        };
        string? issue = problem;
        if (issue is null && value is null)
            issue = window is not null && window.Remaining is not null
                ? UiText.Stale
                : selected is not null ? UiText.MissingSelected
                : UiText.MissingPercentage;
        var other = (snapshot?.Windows ?? []).Where(w => w != window).Select(w =>
        {
            var valid = !failed && w.IsFresh(snapshot!.FetchedAt, now);
            var percent = valid ? w.Remaining : null;
            return new DetailQuotaRow(w.Label, percent is { } p ? Localization.Format(nameof(UiText.RemainingValue), p) : UiText.Unavailable,
                w.ResetsAt is { } reset ? Localization.Date(reset, UiText.ShortDateTimePattern) : UiText.NotReported);
        }).ToArray();
        return new(window?.DurationMinutes == 300 ? UiText.FiveHours : window?.DurationLabel ?? UiText.SelectedQuota,
            window?.BucketName ?? "Codex", value, remaining, tone, status, issue,
            window?.ResetsAt is { } r ? Localization.Date(r, UiText.DatePattern) : UiText.NotReported,
            window?.ResetsAt is { } t ? Localization.Date(t, UiText.TimePattern) : UiText.NoResetDate,
            snapshot is not null ? Localization.Date(snapshot.FetchedAt, UiText.DatePattern) : UiText.NoReading,
            snapshot is not null ? Localization.Date(snapshot.FetchedAt, UiText.ReadTimePattern) : UiText.Waiting,
            refreshing, other, snapshot?.Notes ?? []);
    }
}

using System.Globalization;
using CodexMonitor.Core;

namespace CodexMonitor;

/// <summary>Presentation only. Selection, quota math and freshness use the existing core rules.</summary>
public sealed record DetailQuotaRow(string Label, string Value, string Reset);

public sealed record DetailsViewModel(
    string QuotaLabel, string Bucket, int? Percentage, double? Remaining, BadgeTone Tone,
    string Status, string? Issue, string ResetDate, string ResetTime, string ReadDate, string ReadTime,
    bool Refreshing, IReadOnlyList<DetailQuotaRow> OtherQuotas, IReadOnlyList<string> Notes)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    public const string Cadence = "Actualisation toutes les 60 secondes.";
    public const string Freshness = "Un tiret indique des données indisponibles ou périmées : après 3 minutes sans lecture valide, ou à la réinitialisation.";
    public string PercentageText => Percentage is { } value ? $"{value} %" : "—";
    public string Description => string.Join("\n", new[]
    {
        $"{QuotaLabel} · {Bucket} : {PercentageText} restants. {Status}", Issue,
        $"Réinitialisation : {ResetDate}, {ResetTime}", $"Dernière lecture réussie : {ReadDate}, {ReadTime}",
        Cadence, Freshness,
        string.Join("\n", OtherQuotas.Select(q => $"{q.Label} : {q.Value}. Réinitialisation : {q.Reset}")),
        string.Join("\n", Notes)
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static DetailsViewModel Create(UsageSnapshot? snapshot, string? selected, DateTimeOffset now,
        bool failed, string? problem, bool refreshing)
    {
        var window = snapshot?.Select(selected);
        var value = UsagePresentation.DisplayValue(snapshot, selected, now, failed);
        var remaining = value is null ? null : window?.Remaining;
        var tone = UsagePresentation.Tone(remaining);
        var status = value is null ? "Données indisponibles" : value == 0 ? "Quota épuisé" : tone switch
        {
            BadgeTone.Green => "Quota disponible", BadgeTone.Orange => "À surveiller", _ => "Quota bientôt épuisé"
        };
        string? issue = problem;
        if (issue is null && value is null)
            issue = window is not null && window.Remaining is not null
                ? "La dernière valeur est périmée. Actualisez pour retrouver votre quota."
                : selected is not null ? "Le quota sélectionné n’est pas disponible dans la dernière réponse."
                : "Aucun pourcentage de quota n’a été fourni par Codex pour le moment.";
        var reset = window?.ResetsAt?.ToLocalTime();
        var read = snapshot?.FetchedAt.ToLocalTime();
        var other = (snapshot?.Windows ?? []).Where(w => w != window).Select(w =>
        {
            var valid = !failed && w.IsFresh(snapshot!.FetchedAt, now);
            var percent = valid ? w.Remaining : null;
            return new DetailQuotaRow(w.Label, percent is { } p ? $"{p.ToString("0.##", French)} % restants" : "Indisponible",
                w.ResetsAt?.ToLocalTime().ToString("dd MMM yyyy · HH:mm", French) ?? "Non communiquée");
        }).ToArray();
        return new(window?.DurationMinutes == 300 ? "5 heures" : window?.DurationLabel ?? "Quota sélectionné",
            window?.BucketName ?? "Codex", value, remaining, tone, status, issue,
            reset?.ToString("dd MMMM yyyy", French) ?? "Non communiquée",
            reset?.ToString("HH:mm '· heure locale'", French) ?? "Date non fournie par Codex",
            read?.ToString("dd MMMM yyyy", French) ?? "Aucune lecture réussie",
            read?.ToString("HH:mm:ss '· heure locale'", French) ?? "En attente de Codex",
            refreshing, other, snapshot?.Notes ?? []);
    }
}

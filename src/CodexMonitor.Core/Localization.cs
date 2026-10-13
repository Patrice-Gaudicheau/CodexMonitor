using System.Globalization;
using System.Resources;

namespace CodexMonitor.Core;

public enum LanguagePreference { Automatic, French, English }

/// <summary>UI language and formatting, independent of the user's regional settings.</summary>
public static class Localization
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private sealed record LanguageState(CultureInfo WindowsCulture, LanguagePreference Preference, CultureInfo EffectiveCulture);
    private static LanguageState? state;

    public static CultureInfo SelectCulture(CultureInfo windowsUiCulture, LanguagePreference preference = LanguagePreference.Automatic) =>
        preference switch
        {
            LanguagePreference.French => French,
            LanguagePreference.English => English,
            _ => windowsUiCulture.TwoLetterISOLanguageName == "fr" ? French : English
        };

    // A process-wide state also covers async continuations that captured the previous
    // UI culture before a live switch. They must never restore old-language text.
    public static CultureInfo Culture => Volatile.Read(ref state)?.EffectiveCulture ?? SelectCulture(CultureInfo.CurrentUICulture);
    public static CultureInfo WindowsUiCulture => Volatile.Read(ref state)?.WindowsCulture ?? CultureInfo.CurrentUICulture;
    public static LanguagePreference Preference => Volatile.Read(ref state)?.Preference ?? LanguagePreference.Automatic;

    public static void Initialize(LanguagePreference preference = LanguagePreference.Automatic) =>
        Initialize(CultureInfo.CurrentUICulture, preference);

    public static void Initialize(CultureInfo windowsUiCulture, LanguagePreference preference = LanguagePreference.Automatic)
    {
        var culture = SelectCulture(windowsUiCulture, preference);
        Volatile.Write(ref state, new(windowsUiCulture, preference, culture));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static void ApplyPreference(LanguagePreference preference) => Initialize(WindowsUiCulture, preference);
    public static LanguagePreference ParsePreference(string? value) => value switch
    {
        "fr" => LanguagePreference.French, "en" => LanguagePreference.English, _ => LanguagePreference.Automatic
    };
    public static string PreferenceValue(LanguagePreference preference) => preference switch
    {
        LanguagePreference.French => "fr", LanguagePreference.English => "en", _ => "automatic"
    };

    // Both RESX files deliberately have culture-neutral filenames. The SDK embeds
    // both in Core.dll, rather than generating satellite assemblies. ResourceManager
    // still performs standard .NET resource loading, including in a single-file bundle.
    private static readonly ResourceManager FrenchResources = new("CodexMonitor.Core.Resources.StringsFrench", typeof(Localization).Assembly);
    private static readonly ResourceManager EnglishResources = new("CodexMonitor.Core.Resources.StringsEnglish", typeof(Localization).Assembly);
    public static ResourceManager ResourcesFor(CultureInfo culture) =>
        SelectCulture(culture) == French ? FrenchResources : EnglishResources;

    public static string Get(string key) => ResourcesFor(Culture).GetString(key, CultureInfo.InvariantCulture)
        ?? throw new MissingManifestResourceException($"Missing UI resource: {key}");
    public static string Format(string key, params object?[] arguments) => string.Format(Culture, Get(key), arguments);
    public static string Date(DateTimeOffset value, string pattern) => value.ToLocalTime().ToString(pattern, Culture);
}

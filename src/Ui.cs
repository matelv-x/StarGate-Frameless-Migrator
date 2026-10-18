using System.Globalization;

internal static class Ui
{
    // English is the default. System language and Polish are explicit choices.
    internal static string Language { get; set; } = "en";
    internal static string ResolveLanguage(string selected, CultureInfo culture)
        => selected == "system" ? (culture.TwoLetterISOLanguageName == "pl" ? "pl" : "en") : selected == "pl" ? "pl" : "en";
    internal static string T(string english, string polish)
        => ResolveLanguage(Language, CultureInfo.CurrentUICulture) == "pl" ? polish : english;
}

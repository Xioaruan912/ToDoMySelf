using System.Resources;

namespace PaperTodo;

internal static class ReadingText
{
    private static readonly ResourceManager Manager = new(
        typeof(ReadingText).Assembly.GetName().Name + ".Resources.ReadingStrings",
        typeof(ReadingText).Assembly);
    public static string Get(string key) => Manager.GetString(key, UiLanguages.EffectiveUiCulture) ?? key;
    public static string Format(string key, params object[] args) => string.Format(UiLanguages.EffectiveCulture, Get(key), args);
}

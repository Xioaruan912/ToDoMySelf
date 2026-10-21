using System.Resources;

namespace PaperTodo;

internal static class CopyTranslationStrings
{
    private static readonly ResourceManager Manager = new(
        typeof(CopyTranslationStrings).Assembly.GetName().Name + ".Resources.CopyTranslationStrings",
        typeof(CopyTranslationStrings).Assembly);

    internal static string Get(string key) =>
        Manager.GetString(key, UiLanguages.EffectiveUiCulture) ?? key;
}

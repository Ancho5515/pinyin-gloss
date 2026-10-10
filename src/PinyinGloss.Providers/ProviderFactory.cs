using PinyinGloss.Core;
namespace PinyinGloss.Providers;
public static class ProviderFactory
{
    public static IReadOnlyDictionary<string, ITranslationProvider> Create(AppSettings settings, Action<string>? status = null)
    {
        var providers = new Dictionary<string, ITranslationProvider>();
        foreach (var config in settings.Providers.Where(p => p.Enabled))
        {
            try { providers[config.Id] = config.Kind == ProviderKind.LocalDictionary ? new DictionaryProvider(config) : new HttpModelProvider(config, settings.Response, loadSeconds: settings.Resources.ModelLoadTimeoutSeconds); }
            catch (Exception ex) { status?.Invoke($"{config.Id}: {ex.GetType().Name}"); }
        }
        return providers;
    }
}

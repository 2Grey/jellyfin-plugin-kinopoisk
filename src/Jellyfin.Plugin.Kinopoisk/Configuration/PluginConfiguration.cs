using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Kinopoisk.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // The old service's ApiToken is intentionally not reused for PoiskKino.
        public string PoiskKinoApiToken { get; set; } = string.Empty;
    }
}

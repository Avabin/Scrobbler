namespace Scrobbler;

using System.Text.Json;
using System.Text.Json.Nodes;

public static class ConfigFileHelper
{
    public static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "scrobbler");

    public static readonly string ConfigPath = Path.Combine(ConfigDir, "appsettings.json");

    public static void UpdateConfig(string key, string value) => UpdateConfigNode(key, value);

    public static void UpdateConfig(string key, int value) => UpdateConfigNode(key, value);

    private static void UpdateConfigNode(string key, JsonNode value)
    {
        Directory.CreateDirectory(ConfigDir);

        var json = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : "{}";
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

        var scrobblerSection = root["Scrobbler"]?.AsObject();
        if (scrobblerSection == null)
        {
            scrobblerSection = new JsonObject();
            root["Scrobbler"] = scrobblerSection;
        }

        scrobblerSection[key] = value;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(ConfigPath, root.ToJsonString(options));
    }

    public static void EnsureDefaultConfig()
    {
        if (File.Exists(ConfigPath)) return;

        Directory.CreateDirectory(ConfigDir);

        var defaults = new JsonObject
        {
            ["Scrobbler"] = new JsonObject
            {
                ["ApiKey"] = "",
                ["ApiSecret"] = "",
                ["SessionKey"] = "",
                ["ScrobblePercentage"] = 50,
                ["PreferredPlayer"] = "",
                ["PollingIntervalMs"] = 5000
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(ConfigPath, defaults.ToJsonString(options));
    }
}

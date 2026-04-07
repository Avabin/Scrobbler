namespace Scrobbler.Cli;

using System.Text.Json;
using System.Text.Json.Nodes;
using ConsoleAppFramework;
using Scrobbler.Cli.DBus;

/// <summary>
/// Scrobbler CLI - manage the scrobbling daemon.
/// </summary>
[RegisterCommands("list")]
public class ListCommands
{
    /// <summary>
    /// List all discovered music players available for scrobbling.
    /// </summary>
    [Command("sources")]
    public async Task Sources()
    {
        var daemon = await DaemonProxy.ConnectAsync();
        var sources = await daemon.ListSourcesAsync();

        if (sources.Length == 0)
        {
            Console.WriteLine("No music players found.");
            return;
        }

        var selected = await daemon.GetSelectedSourceAsync();

        Console.WriteLine("Available music players:");
        foreach (var source in sources)
        {
            var marker = string.Equals(source, selected, StringComparison.OrdinalIgnoreCase) ? " *" : "";
            Console.WriteLine($"  {source}{marker}");
        }

        if (!string.IsNullOrEmpty(selected))
            Console.WriteLine($"\n* = currently selected");
    }
}

/// <summary>
/// Set daemon configuration.
/// </summary>
[RegisterCommands("set")]
public class SetCommands
{
    /// <summary>
    /// Set the preferred music player for scrobbling.
    /// </summary>
    /// <param name="name">The player name (e.g. spotify, firefox, vlc).</param>
    [Command("source")]
    public async Task Source(string name)
    {
        var daemon = await DaemonProxy.ConnectAsync();
        await daemon.SetSourceAsync(name);
        Console.WriteLine($"Preferred player set to: {name}");
    }

    /// <summary>
    /// Set the minimum scrobble percentage (0-100).
    /// </summary>
    /// <param name="percent">Percentage of track that must be played before scrobbling (0-100).</param>
    [Command("scrobble-percent")]
    public async Task ScrobblePercent([System.ComponentModel.DataAnnotations.Range(0, 100)] int percent)
    {
        var daemon = await DaemonProxy.ConnectAsync();
        await daemon.SetScrobblePercentAsync(percent);
        Console.WriteLine($"Scrobble percentage set to: {percent}%");
    }

    /// <summary>
    /// Set the Last.fm API key.
    /// </summary>
    /// <param name="key">The Last.fm API key.</param>
    [Command("api-key")]
    public Task ApiKey(string key)
    {
        SaveToConfig("ApiKey", key);
        Console.WriteLine("API key saved.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Set the Last.fm API secret.
    /// </summary>
    /// <param name="secret">The Last.fm API secret.</param>
    [Command("api-secret")]
    public Task ApiSecret(string secret)
    {
        SaveToConfig("ApiSecret", secret);
        Console.WriteLine("API secret saved.");
        return Task.CompletedTask;
    }

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "scrobbler");

    private static void SaveToConfig(string key, string value)
    {
        Directory.CreateDirectory(ConfigDir);
        var configPath = Path.Combine(ConfigDir, "appsettings.json");
        var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

        var section = root["Scrobbler"]?.AsObject();
        if (section == null)
        {
            section = new JsonObject();
            root["Scrobbler"] = section;
        }

        section[key] = value;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(configPath, root.ToJsonString(options));
    }
}

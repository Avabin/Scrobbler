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
        for (var i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            var marker = string.Equals(source, selected, StringComparison.OrdinalIgnoreCase) ? " *" : "";
            Console.WriteLine($"  {i + 1}. {source}{marker}");
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
    /// <param name="index">The 1-based player index shown by `list sources`.</param>
    /// <param name="playerName">-n|--name, The player name (e.g. spotify, firefox, vlc).</param>
    [Command("source")]
    public async Task Source([Argument] int? index = null, string? playerName = null)
    {
        var daemon = await DaemonProxy.ConnectAsync();
        var sources = await daemon.ListSourcesAsync();

        if (index is null && string.IsNullOrWhiteSpace(playerName))
        {
            Console.WriteLine("Specify either a player index or --name.");
            return;
        }

        if (index is not null && !string.IsNullOrWhiteSpace(playerName))
        {
            Console.WriteLine("Specify either a player index or --name, not both.");
            return;
        }

        if (sources.Length == 0)
        {
            Console.WriteLine("No music players found.");
            return;
        }

        string sourceName;
        if (index is not null)
        {
            var sourceIndex = index.Value - 1;
            if (sourceIndex < 0 || sourceIndex >= sources.Length)
            {
                Console.WriteLine($"Invalid player index: {index}. Use 'scrbl-cli list sources' to see valid indices.");
                return;
            }

            sourceName = sources[sourceIndex];
        }
        else
        {
            sourceName = sources.FirstOrDefault(source =>
                string.Equals(source, playerName, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

            if (string.IsNullOrEmpty(sourceName))
            {
                Console.WriteLine($"Unknown player name: {playerName}. Use 'scrbl-cli list sources' to see discovered players.");
                return;
            }
        }

        await daemon.SetSourceAsync(sourceName);
        Console.WriteLine($"Preferred player set to: {sourceName}");
    }

    /// <summary>
    /// Set the minimum scrobble percentage (0-100).
    /// </summary>
    /// <param name="percent">Percentage of track that must be played before scrobbling (0-100).</param>
    [Command("scrobble-percent")]
    public async Task ScrobblePercent([Argument][System.ComponentModel.DataAnnotations.Range(0, 100)] int percent)
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
    public Task ApiKey([Argument] string key)
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
    public Task ApiSecret([Argument] string secret)
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

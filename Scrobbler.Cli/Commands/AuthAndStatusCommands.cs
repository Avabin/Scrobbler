namespace Scrobbler.Cli.Commands;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConsoleAppFramework;
using Hqub.Lastfm;
using Scrobbler.Cli.DBus;

/// <summary>
/// Authentication and status commands.
/// </summary>
[RegisterCommands]
public class AuthAndStatusCommands
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "scrobbler");

    /// <summary>
    /// Authenticate with Last.fm by opening the browser for authorization.
    /// If API key/secret are already saved in config, they are used automatically.
    /// </summary>
    /// <param name="apiKey">-k, Last.fm API key (optional if already configured).</param>
    /// <param name="apiSecret">-s, Last.fm API secret (optional if already configured).</param>
    [Command("auth")]
    public async Task Auth(string? apiKey = null, string? apiSecret = null)
    {
        // Try loading from config if not provided
        var configPath = Path.Combine(ConfigDir, "appsettings.json");
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            var (savedKey, savedSecret) = LoadCredentials(configPath);
            apiKey ??= savedKey;
            apiSecret ??= savedSecret;
        }

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            Console.WriteLine("API key and secret are required. Provide them via -k/-s flags or run 'set api-key' and 'set api-secret' first.");
            return;
        }

        var client = new LastfmClient(apiKey, apiSecret);

        // 1. Get a request token and auth URL
        Console.WriteLine("Requesting authorization token...");
        var authUrl = await client.GetWebAuthenticationUrlAsync();

        // 2. Open browser for user to authorize
        Console.WriteLine("Opening browser for Last.fm authorization...");
        Console.WriteLine($"If the browser doesn't open, visit:\n  {authUrl}");

        OpenBrowser(authUrl);

        // 3. Wait for user to authorize in browser
        Console.WriteLine("\nAfter authorizing in the browser, press Enter to continue...");
        Console.ReadLine();

        // 4. Exchange token for session (auth.getSession)
        Console.WriteLine("Completing authentication...");
        await client.AuthenticateViaWebAsync();

        var sessionKey = client.Session.SessionKey;
        if (string.IsNullOrEmpty(sessionKey))
        {
            Console.WriteLine("Authentication failed. No session key received.");
            return;
        }

        // 5. Save credentials to shared config
        Directory.CreateDirectory(ConfigDir);
        SaveToConfig(configPath, "ApiKey", apiKey);
        SaveToConfig(configPath, "ApiSecret", apiSecret);
        SaveToConfig(configPath, "SessionKey", sessionKey);
        // Also save session-key file for daemon backwards compat
        await File.WriteAllTextAsync(Path.Combine(ConfigDir, "session-key"), sessionKey);
        Console.WriteLine($"Credentials saved to {configPath}");

        // 6. Notify daemon if running
        try
        {
            var daemon = await DaemonProxy.ConnectAsync();
            await daemon.NotifyAuthenticatedAsync(sessionKey);
            Console.WriteLine("Daemon notified of new authentication.");
        }
        catch
        {
            Console.WriteLine("Daemon is not running. Session key will be used when daemon starts.");
        }

        Console.WriteLine("Authentication successful!");
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start("xdg-open", url); }
            catch { /* user will open manually */ }
        }
    }

    /// <summary>
    /// Show scrobbler daemon status: current track, last scrobbled, connection, players.
    /// </summary>
    [Command("status")]
    public async Task Status()
    {
        Daemon daemon;
        try
        {
            daemon = await DaemonProxy.ConnectAsync();
        }
        catch
        {
            Console.WriteLine("Daemon is not running.");
            return;
        }

        string statusJson;
        try
        {
            statusJson = await daemon.GetStatusJsonAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get status: {ex.Message}");
            return;
        }

        using var doc = JsonDocument.Parse(statusJson);
        var root = doc.RootElement;

        var connected = root.GetProperty("connected").GetBoolean();
        var selectedPlayer = root.GetProperty("selectedPlayer").GetString();
        var scrobblePercent = root.GetProperty("scrobblePercentage").GetInt32();

        Console.WriteLine($"Last.fm connection:  {(connected ? "Authenticated" : "Not authenticated")}");
        Console.WriteLine($"Scrobble threshold:  {scrobblePercent}%");
        Console.WriteLine($"Selected player:     {(string.IsNullOrEmpty(selectedPlayer) ? "(auto)" : selectedPlayer)}");

        // All players
        if (root.TryGetProperty("allPlayers", out var playersEl))
        {
            var players = playersEl.EnumerateArray().Select(p => p.GetString()).ToArray();
            Console.WriteLine($"Available players:   {(players.Length > 0 ? string.Join(", ", players) : "none")}");
        }

        // Currently playing
        Console.WriteLine();
        if (root.TryGetProperty("currentlyPlaying", out var nowEl) && nowEl.ValueKind != JsonValueKind.Null)
        {
            var artist = nowEl.GetProperty("artist").GetString();
            var title = nowEl.GetProperty("title").GetString();
            var album = nowEl.GetProperty("album").GetString();
            var player = nowEl.GetProperty("player").GetString();
            var playback = nowEl.GetProperty("playbackStatus").GetString();

            Console.WriteLine($"Currently playing:   {artist} - {title}");
            if (!string.IsNullOrEmpty(album))
                Console.WriteLine($"  Album:             {album}");

            if (nowEl.TryGetProperty("durationMs", out var durEl))
            {
                var durationMs = durEl.GetInt64();
                var playTimeMs = nowEl.GetProperty("playTimeMs").GetInt64();
                var positionMs = nowEl.GetProperty("positionMs").GetInt64();
                var scrobbled = nowEl.GetProperty("scrobbled").GetBoolean();

                var duration = TimeSpan.FromMilliseconds(durationMs);
                var position = TimeSpan.FromMilliseconds(positionMs);
                var playTime = TimeSpan.FromMilliseconds(playTimeMs);

                if (durationMs > 0)
                {
                    var pct = (int)(playTimeMs * 100 / durationMs);
                    Console.WriteLine($"  Duration:          {duration.ToString(@"m\:ss")}");
                    Console.WriteLine($"  Position:          {position.ToString(@"m\:ss")}");
                    Console.WriteLine($"  Play time:         {playTime.ToString(@"m\:ss")} ({pct}%)");
                }
                else
                {
                    Console.WriteLine($"  Play time:         {playTime.ToString(@"m\:ss")}");
                }

                Console.WriteLine($"  Scrobbled:         {(scrobbled ? "Yes" : "Not yet")}");
            }

            Console.WriteLine($"  Player:            {player} ({playback})");
        }
        else
        {
            Console.WriteLine("Currently playing:   Nothing");
        }

        // Last scrobbled
        if (root.TryGetProperty("lastScrobbled", out var lastEl) && lastEl.ValueKind != JsonValueKind.Null)
        {
            Console.WriteLine($"Last scrobbled:      {lastEl.GetString()}");
        }
    }

    private static void SaveToConfig(string configPath, string key, string value)
    {
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

    private static (string? apiKey, string? apiSecret) LoadCredentials(string configPath)
    {
        if (!File.Exists(configPath)) return (null, null);

        var json = File.ReadAllText(configPath);
        var root = JsonNode.Parse(json)?.AsObject();
        var section = root?["Scrobbler"]?.AsObject();
        if (section == null) return (null, null);

        var key = section["ApiKey"]?.GetValue<string>();
        var secret = section["ApiSecret"]?.GetValue<string>();

        return (
            string.IsNullOrEmpty(key) ? null : key,
            string.IsNullOrEmpty(secret) ? null : secret
        );
    }
}

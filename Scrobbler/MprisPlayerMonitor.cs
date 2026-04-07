namespace Scrobbler;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scrobbler.DBus.Mpris;
using Tmds.DBus.Protocol;

public record TrackInfo(string TrackId, string Title, string Artist, string Album, long LengthMicroseconds);

public record PlayerState(string PlayerName, string PlaybackStatus, TrackInfo? Track, long PositionMicroseconds);

public class MprisPlayerMonitor : IDisposable
{
    private readonly ILogger<MprisPlayerMonitor> _logger;
    private readonly IOptionsMonitor<ScrobblerConfig> _config;
    private DBusConnection? _connection;

    public MprisPlayerMonitor(ILogger<MprisPlayerMonitor> logger, IOptionsMonitor<ScrobblerConfig> config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task ConnectAsync()
    {
        _connection = new DBusConnection(DBusAddress.Session!);
        await _connection.ConnectAsync();
        _logger.LogInformation("Connected to D-Bus session bus");
    }

    public async Task<string[]> GetAvailablePlayersAsync()
    {
        if (_connection == null) throw new InvalidOperationException("Not connected to D-Bus");

        var names = await _connection.ListServicesAsync();
        return names
            .Where(n => n.StartsWith("org.mpris.MediaPlayer2."))
            .ToArray();
    }

    public async Task<PlayerState?> GetPlayerStateAsync()
    {
        if (_connection == null) return null;

        var players = await GetAvailablePlayersAsync();
        if (players.Length == 0) return null;

        var selectedPlayer = SelectPlayer(players);
        if (selectedPlayer == null) return null;

        try
        {
            var player = new Player(_connection, selectedPlayer, "/org/mpris/MediaPlayer2");

            var status = await player.GetPlaybackStatusAsync();
            var metadata = await player.GetMetadataAsync();

            long position = 0;
            try
            {
                position = await player.GetPositionAsync();
            }
            catch
            {
                // Some players don't support Position property
            }

            var trackInfo = ParseMetadata(metadata);
            var shortName = selectedPlayer.Replace("org.mpris.MediaPlayer2.", "");

            return new PlayerState(shortName, status, trackInfo, position);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get state from player {Player}", selectedPlayer);
            return null;
        }
    }

    private string? SelectPlayer(string[] players)
    {
        var preferred = _config.CurrentValue.PreferredPlayer;

        if (!string.IsNullOrEmpty(preferred))
        {
            var match = players.FirstOrDefault(p =>
                p.Equals($"org.mpris.MediaPlayer2.{preferred}", StringComparison.OrdinalIgnoreCase) ||
                p.Contains(preferred, StringComparison.OrdinalIgnoreCase));

            if (match != null) return match;

            _logger.LogDebug("Preferred player '{Preferred}' not found among: {Players}",
                preferred, string.Join(", ", players));
        }

        return players.FirstOrDefault();
    }

    private TrackInfo? ParseMetadata(Dictionary<string, VariantValue>? metadata)
    {
        if (metadata == null || metadata.Count == 0) return null;

        var trackId = GetMetadataString(metadata, "mpris:trackid");
        var title = GetMetadataString(metadata, "xesam:title");
        var artist = GetMetadataStringArray(metadata, "xesam:artist");
        var album = GetMetadataString(metadata, "xesam:album");
        var length = GetMetadataLong(metadata, "mpris:length");

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
            return null;

        return new TrackInfo(trackId, title, artist, album, length);
    }

    private static string GetMetadataString(Dictionary<string, VariantValue> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value)) return "";
        try
        {
            if (value.Type == VariantValueType.ObjectPath)
                return value.GetObjectPathAsString();
            return value.GetString();
        }
        catch { return value.ToString() ?? ""; }
    }

    private static string GetMetadataStringArray(Dictionary<string, VariantValue> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value)) return "";

        try
        {
            if (value.Type == VariantValueType.Array)
            {
                var arr = value.GetArray<string>();
                return string.Join(", ", arr);
            }
            return value.GetString();
        }
        catch { return value.ToString() ?? ""; }
    }

    private static long GetMetadataLong(Dictionary<string, VariantValue> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value)) return 0;

        try
        {
            return value.Type switch
            {
                VariantValueType.Int64 => value.GetInt64(),
                VariantValueType.Int32 => value.GetInt32(),
                VariantValueType.UInt64 => (long)value.GetUInt64(),
                VariantValueType.Double => (long)value.GetDouble(),
                _ => long.TryParse(value.ToString(), out var parsed) ? parsed : 0
            };
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}

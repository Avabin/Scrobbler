namespace Scrobbler.DBus;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tmds.DBus.Protocol;

public class ScrobblerDaemonObject : IPathMethodHandler
{
    private readonly MprisPlayerMonitor _mpris;
    private readonly IOptionsMonitor<ScrobblerConfig> _config;
    private readonly ILogger<ScrobblerDaemonObject> _logger;
    private readonly ScrobblingService _scrobblingService;

    private static readonly ReadOnlyMemory<byte> InterfaceXml = Encoding.UTF8.GetBytes(
        """
        <interface name="org.scrobbler.Daemon">
          <method name="ListSources"><arg name="sources" type="as" direction="out"/></method>
          <method name="GetSelectedSource"><arg name="source" type="s" direction="out"/></method>
          <method name="SetSource"><arg name="playerName" type="s" direction="in"/></method>
          <method name="GetScrobblePercent"><arg name="percent" type="i" direction="out"/></method>
          <method name="SetScrobblePercent"><arg name="percent" type="i" direction="in"/></method>
          <method name="GetStatusJson"><arg name="status" type="s" direction="out"/></method>
          <method name="NotifyAuthenticated"><arg name="sessionKey" type="s" direction="in"/></method>
        </interface>
        """);

    public string Path => ScrobblerDBusConstants.ObjectPath;
    public bool HandlesChildPaths => false;

    public ScrobblerDaemonObject(
        MprisPlayerMonitor mpris,
        IOptionsMonitor<ScrobblerConfig> config,
        ILogger<ScrobblerDaemonObject> logger,
        ScrobblingService scrobblingService)
    {
        _mpris = mpris;
        _config = config;
        _logger = logger;
        _scrobblingService = scrobblingService;
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            _logger.LogDebug("Received D-Bus introspection request for {Path}", context.Request.PathAsString);
            context.ReplyIntrospectXml([InterfaceXml]);
            return default;
        }

        var request = context.Request;
        var iface = request.InterfaceAsString;
        var member = request.MemberAsString;
        var signature = request.SignatureAsString;

        _logger.LogInformation(
            "Received D-Bus call path={Path} interface={Interface} member={Member} signature={Signature}",
            request.PathAsString,
            iface,
            member,
            signature);

        if (iface != "org.scrobbler.Daemon")
        {
            _logger.LogWarning(
                "Rejecting D-Bus call due to unexpected interface {Interface} for member {Member}",
                iface,
                member);
            context.ReplyUnknownMethodError();
            return default;
        }

        return (member, signature) switch
        {
            ("ListSources", "") => HandleListSourcesAsync(context),
            ("GetSelectedSource", "") => HandleGetSelectedSourceAsync(context),
            ("SetSource", "s") => HandleSetSourceAsync(context),
            ("GetScrobblePercent", "") => HandleGetScrobblePercentAsync(context),
            ("SetScrobblePercent", "i") => HandleSetScrobblePercentAsync(context),
            ("GetStatusJson", "") => HandleGetStatusJsonAsync(context),
            ("NotifyAuthenticated", "s") => HandleNotifyAuthenticatedAsync(context),
            _ => ReplyUnknown(context)
        };
    }

    private static ValueTask ReplyUnknown(MethodContext context)
    {
        // Let the caller see the exact member/signature that failed to match.
        context.ReplyUnknownMethodError();
        return default;
    }

    private async ValueTask HandleListSourcesAsync(MethodContext context)
    {
        context.DisposesAsynchronously = true;
        try
        {
            var players = await _mpris.GetAvailablePlayersAsync();
            var sources = players
                .Select(p => p.Replace("org.mpris.MediaPlayer2.", ""))
                .ToArray();

            using var writer = context.CreateReplyWriter("as");
            writer.WriteArray(sources);
            context.Reply(writer.CreateMessage());
        }
        catch (Exception ex)
        {
            ReplyInternalError(context, ex, "ListSources");
        }
        finally
        {
            context.Dispose();
        }
    }

    private ValueTask HandleGetSelectedSourceAsync(MethodContext context)
    {
        using var writer = context.CreateReplyWriter("s");
        writer.WriteString(_config.CurrentValue.PreferredPlayer);
        context.Reply(writer.CreateMessage());
        return default;
    }

    private ValueTask HandleSetSourceAsync(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var playerName = reader.ReadString();
        _logger.LogInformation("Setting preferred player to '{Player}' via D-Bus", playerName);
        ConfigFileHelper.UpdateConfig("PreferredPlayer", playerName);
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
        return default;
    }

    private ValueTask HandleGetScrobblePercentAsync(MethodContext context)
    {
        using var writer = context.CreateReplyWriter("i");
        writer.WriteInt32(_config.CurrentValue.ScrobblePercentage);
        context.Reply(writer.CreateMessage());
        return default;
    }

    private ValueTask HandleSetScrobblePercentAsync(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var percent = reader.ReadInt32();

        if (percent is < 0 or > 100)
        {
            context.ReplyError("org.scrobbler.Error.InvalidArgument", "Must be between 0 and 100");
            return default;
        }

        _logger.LogInformation("Setting scrobble percentage to {Pct}% via D-Bus", percent);
        ConfigFileHelper.UpdateConfig("ScrobblePercentage", percent);
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
        return default;
    }

    private async ValueTask HandleGetStatusJsonAsync(MethodContext context)
    {
        context.DisposesAsynchronously = true;
        try
        {
            var state = await _mpris.GetPlayerStateAsync();
            var players = await _mpris.GetAvailablePlayersAsync();
            var shortPlayers = players.Select(p => p.Replace("org.mpris.MediaPlayer2.", "")).ToArray();

            var status = new StatusResponse
            {
                Connected = _scrobblingService.IsAuthenticated,
                SelectedPlayer = _config.CurrentValue.PreferredPlayer,
                AllPlayers = shortPlayers,
                CurrentlyPlaying = state?.Track != null ? new CurrentlyPlayingStatus
                {
                    Artist = state.Track.Artist,
                    Title = state.Track.Title,
                    Album = state.Track.Album,
                    Player = state.PlayerName,
                    PlaybackStatus = state.PlaybackStatus,
                    DurationMs = state.Track.LengthMicroseconds / 1000,
                    PositionMs = state.PositionMicroseconds / 1000,
                    PlayTimeMs = (long)_scrobblingService.PlayTime.TotalMilliseconds,
                    Scrobbled = _scrobblingService.Scrobbled
                } : null,
                LastScrobbled = _scrobblingService.LastScrobbledInfo,
                ScrobblePercentage = _config.CurrentValue.ScrobblePercentage
            };

            var json = JsonSerializer.Serialize(status, ScrobblerJsonContext.Default.StatusResponse);

            using var writer = context.CreateReplyWriter("s");
            writer.WriteString(json);
            context.Reply(writer.CreateMessage());
        }
        catch (Exception ex)
        {
            ReplyInternalError(context, ex, "GetStatusJson");
        }
        finally
        {
            context.Dispose();
        }
    }

    private ValueTask HandleNotifyAuthenticatedAsync(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var sessionKey = reader.ReadString();
        _logger.LogInformation("Received new session key via D-Bus");
        _scrobblingService.UpdateSessionKey(sessionKey);
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
        return default;
    }

    private void ReplyInternalError(MethodContext context, Exception ex, string methodName)
    {
        _logger.LogError(ex, "Unhandled exception while processing D-Bus method {Method}", methodName);

        if (!context.ReplySent && !context.NoReplyExpected)
        {
            context.ReplyError("org.scrobbler.Error.Internal", ex.Message);
        }
    }
}

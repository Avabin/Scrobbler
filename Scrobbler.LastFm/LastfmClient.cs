namespace Scrobbler.LastFm;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

/// <summary>
/// A Last.fm API client implemented directly on <see cref="HttpClient"/>.
///
/// Supports the desktop/web authentication flow (<c>auth.getToken</c> +
/// <c>auth.getSession</c>), the mobile flow (<c>auth.getMobileSession</c>), and
/// the track write services (<c>track.updateNowPlaying</c>, <c>track.scrobble</c>).
/// Every request is signed per-call from that call's own parameter set, per
/// https://www.last.fm/api/authspec#_8-signing-calls.
/// </summary>
public sealed class LastfmClient : IDisposable
{
    /// <summary>Base endpoint for all (read and write) web service requests.</summary>
    public const string Endpoint = "https://ws.audioscrobbler.com/2.0/";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private bool _ownsHttpClient;
    private string? _pendingToken;

    /// <summary>The authenticated user's session key. Required for write services.</summary>
    public string? SessionKey { get; set; }

    public LastfmClient(string apiKey, string apiSecret, HttpClient? http = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _apiSecret = apiSecret ?? throw new ArgumentNullException(nameof(apiSecret));

        if (http != null)
        {
            _http = http;
        }
        else
        {
            _http = new HttpClient { BaseAddress = new Uri(Endpoint) };
            _ownsHttpClient = true;
        }
    }

    /* ------------------------------------------------------------------ *
     * Authentication
     * ------------------------------------------------------------------ */

    /// <summary>
    /// The last-fm user this client is authenticated as (from the session response).
    /// </summary>
    public string? SessionUsername { get; private set; }

    /// <summary>
    /// Fetches an un-authorised request token via <c>auth.getToken</c>.
    /// The token has to be authorised by the user in a browser before it can be
    /// exchanged for a session key via <see cref="GetSessionKeyAsync"/>.
    /// </summary>
    public async Task<string> GetRequestTokenAsync(CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string> { ["method"] = "auth.getToken" };
        var document = await PostAsync(parameters, sessionKeyRequired: false, cancellationToken).ConfigureAwait(false);

        var token = document.Root?.Element("token")?.Value?.Trim();
        if (string.IsNullOrEmpty(token))
            throw new LastfmApiException(0, "Last.fm returned no request token.");

        return token;
    }

    /// <summary>
    /// Builds the Last.fm authorization URL for the given request token.
    /// Send the user to this URL; they log in, click <c>Allow</c>, and the token
    /// becomes authorised.
    /// </summary>
    public string BuildWebAuthUrl(string token)
        => $"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(_apiKey)}&token={Uri.EscapeDataString(token)}";

    /// <summary>
    /// Fetches a request token and returns the authorization URL for the desktop/web
    /// flow. The returned token is remembered internally so <see cref="GetWebSessionAsync"/>
    /// can exchange it for a session key after the user authorizes.
    /// </summary>
    public async Task<string> GetWebAuthenticationUrlAsync(CancellationToken cancellationToken = default)
    {
        _pendingToken = await GetRequestTokenAsync(cancellationToken).ConfigureAwait(false);
        return BuildWebAuthUrl(_pendingToken);
    }

    /// <summary>
    /// Exchanges an authorised request token for a session key via <c>auth.getSession</c>.
    /// A token can only be used once.
    /// </summary>
    public async Task<string> GetSessionKeyAsync(string token, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["method"] = "auth.getSession",
            ["token"] = token
        };
        var document = await PostAsync(parameters, sessionKeyRequired: false, cancellationToken).ConfigureAwait(false);

        return ParseSession(document);
    }

    /// <summary>
    /// Exchanges the token fetched by <see cref="GetWebAuthenticationUrlAsync"/> for a
    /// session key. Call this after the user has authorised the token in the browser.
    /// The pending token is cleared on use — tokens are single-use.
    /// </summary>
    public async Task<string> GetWebSessionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_pendingToken))
            throw new InvalidOperationException("No pending request token. Call GetWebAuthenticationUrlAsync first.");
        var token = _pendingToken!;
        _pendingToken = null;
        return await GetSessionKeyAsync(token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Authenticates using username/password via <c>auth.getMobileSession</c>
    /// (mobile/standalone-device flow). Must be sent over POST + HTTPS.
    /// </summary>
    public async Task<string> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["method"] = "auth.getMobileSession",
            ["username"] = username,
            ["password"] = password
        };
        var document = await PostAsync(parameters, sessionKeyRequired: false, cancellationToken).ConfigureAwait(false);

        return ParseSession(document);
    }

    private string ParseSession(XDocument document)
    {
        var session = document.Root?.Element("session");
        var key = session?.Element("key")?.Value?.Trim();
        if (string.IsNullOrEmpty(key))
            throw new LastfmApiException(0, "Last.fm returned no session key.");

        SessionKey = key;
        SessionUsername = session?.Element("name")?.Value?.Trim();
        return key;
    }

    /* ------------------------------------------------------------------ *
     * Track methods
     * ------------------------------------------------------------------ */

    /// <summary>
    /// Notifies Last.fm that a user has started listening to a track
    /// (<c>track.updateNowPlaying</c>).
    /// </summary>
    public async Task<NowPlayingResponse> UpdateNowPlayingAsync(
        string artist,
        string track,
        string? album = null,
        string? albumArtist = null,
        string? musicBrainzId = null,
        int? trackNumber = null,
        int? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["method"] = "track.updateNowPlaying",
            ["artist"] = artist,
            ["track"] = track
        };

        AddIfPresent(parameters, "album", album);
        AddIfPresent(parameters, "albumArtist", albumArtist);
        AddIfPresent(parameters, "mbid", musicBrainzId);
        AddIfPresent(parameters, "trackNumber", trackNumber);
        AddIfPresent(parameters, "duration", durationSeconds);

        var response = await SendWriteRequestAsync(parameters, cancellationToken).ConfigureAwait(false);
        return ParseNowPlaying(response);
    }

    /// <summary>
    /// Adds one or more track-plays to the user's profile (<c>track.scrobble</c>).
    /// Batch requests may contain up to 50 scrobbles.
    /// </summary>
    public async Task<ScrobbleResponse> ScrobbleAsync(
        IReadOnlyList<Scrobble> scrobbles,
        CancellationToken cancellationToken = default)
    {
        if (scrobbles == null || scrobbles.Count == 0)
            throw new ArgumentException("At least one scrobble must be provided.", nameof(scrobbles));
        if (scrobbles.Count > 50)
            throw new ArgumentException("A batch may contain at most 50 scrobbles.", nameof(scrobbles));

        var parameters = new Dictionary<string, string>
        {
            ["method"] = "track.scrobble"
        };

        for (var i = 0; i < scrobbles.Count; i++)
        {
            var s = scrobbles[i];
            var idx = i.ToString(CultureInfo.InvariantCulture);
            parameters[$"artist[{idx}]"] = s.Artist;
            parameters[$"track[{idx}]"] = s.Track;
            parameters[$"timestamp[{idx}]"] = ToUnixSeconds(s.StartedAtUtc).ToString(CultureInfo.InvariantCulture);

            AddIfPresent(parameters, $"album[{idx}]", s.Album);
            AddIfPresent(parameters, $"albumArtist[{idx}]", s.AlbumArtist);
            AddIfPresent(parameters, $"mbid[{idx}]", s.MusicBrainzId);
            AddIfPresent(parameters, $"trackNumber[{idx}]", s.TrackNumber);
            AddIfPresent(parameters, $"duration[{idx}]", s.DurationSeconds);
            if (s.ChosenByUser is { } chosen)
                parameters[$"chosenByUser[{idx}]"] = chosen ? "1" : "0";
        }

        var response = await SendWriteRequestAsync(parameters, cancellationToken).ConfigureAwait(false);
        return ParseScrobbleResponse(response);
    }

    /// <summary>
    /// Scrobbles a single track.
    /// </summary>
    public Task<ScrobbleResponse> ScrobbleAsync(Scrobble scrobble, CancellationToken cancellationToken = default)
        => ScrobbleAsync([scrobble], cancellationToken);

    /* ------------------------------------------------------------------ *
     * Request plumbing
     * ------------------------------------------------------------------ */

    private Task<XDocument> SendWriteRequestAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
        => PostAsync(parameters, sessionKeyRequired: true, cancellationToken);

    private async Task<XDocument> PostAsync(
        Dictionary<string, string> parameters,
        bool sessionKeyRequired,
        CancellationToken cancellationToken)
    {
        if (sessionKeyRequired && string.IsNullOrEmpty(SessionKey))
            throw new InvalidOperationException("A session key is required for write services. Authenticate first.");

        parameters["api_key"] = _apiKey;
        if (sessionKeyRequired)
            parameters["sk"] = SessionKey!;
        parameters["api_sig"] = Sign(parameters);

        using var content = new FormUrlEncodedContent(parameters);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var document = XDocument.Parse(body);

        var status = document.Root?.Attribute("status")?.Value;
        if (status == "failed")
            throw ParseError(document);

        return document;
    }

    private string Sign(Dictionary<string, string> parameters)
    {
        // Signature is the MD5 of the concatenation of sorted "name+value" pairs
        // followed by the secret. Sorting must be by ASCII (ordinal) byte order.
        var builder = new StringBuilder();
        foreach (var param in parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(param.Key);
            builder.Append(param.Value);
        }
        builder.Append(_apiSecret);

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    private static LastfmApiException ParseError(XDocument document)
    {
        var error = document.Root?.Element("error");
        var codeText = error?.Attribute("code")?.Value;
        var code = int.TryParse(codeText, out var c) ? c : 0;
        var message = error?.Value?.Trim() ?? "Unknown Last.fm error";
        return new LastfmApiException(code, message);
    }

    private static NowPlayingResponse ParseNowPlaying(XDocument document)
    {
        var nowPlaying = document.Root?.Element("nowplaying");
        if (nowPlaying == null)
            return new NowPlayingResponse();

        return new NowPlayingResponse
        {
            Track = ElementValue(nowPlaying, "track"),
            Artist = ElementValue(nowPlaying, "artist"),
            Album = ElementValue(nowPlaying, "album"),
            IgnoredCode = IgnoredCode(nowPlaying)
        };
    }

    private static ScrobbleResponse ParseScrobbleResponse(XDocument document)
    {
        var scrobbles = document.Root?.Element("scrobbles");
        if (scrobbles == null)
            return new ScrobbleResponse();

        var accepted = int.TryParse(scrobbles.Attribute("accepted")?.Value, out var a) ? a : 0;
        var ignored = int.TryParse(scrobbles.Attribute("ignored")?.Value, out var i) ? i : 0;

        var results = scrobbles
            .Elements("scrobble")
            .Select(ParseScrobbleResult)
            .ToArray();

        return new ScrobbleResponse
        {
            Accepted = accepted,
            Ignored = ignored,
            Results = results
        };
    }

    private static ScrobbleResult ParseScrobbleResult(XElement scrobble)
    {
        var ignoredCode = IgnoredCode(scrobble);
        return new ScrobbleResult
        {
            Track = ElementValue(scrobble, "track"),
            Artist = ElementValue(scrobble, "artist"),
            Album = ElementValue(scrobble, "album"),
            TrackCorrected = ElementCorrected(scrobble, "track"),
            ArtistCorrected = ElementCorrected(scrobble, "artist"),
            AlbumCorrected = ElementCorrected(scrobble, "album"),
            IgnoredCode = ignoredCode,
            IgnoredMessage = IgnoredMessage(scrobble)
        };
    }

    private static int IgnoredCode(XElement container)
    {
        var ignoredMessage = container.Element("ignoredMessage");
        if (ignoredMessage == null) return 0;
        return int.TryParse(ignoredMessage.Attribute("code")?.Value, out var code) ? code : 0;
    }

    private static string IgnoredMessage(XElement container)
    {
        return container.Element("ignoredMessage")?.Value?.Trim() ?? "";
    }

    private static string ElementValue(XElement container, string name)
    {
        return container.Element(name)?.Value ?? "";
    }

    private static bool ElementCorrected(XElement container, string name)
    {
        return container.Element(name)?.Attribute("corrected")?.Value == "1";
    }

    private static void AddIfPresent(Dictionary<string, string> parameters, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            parameters[key] = value;
    }

    private static void AddIfPresent(Dictionary<string, string> parameters, string key, int? value)
    {
        if (value is { } v)
            parameters[key] = v.ToString(CultureInfo.InvariantCulture);
    }

    private static long ToUnixSeconds(DateTime dateTime)
    {
        return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}

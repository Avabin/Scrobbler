namespace Scrobbler;

using Hqub.Lastfm;
using Hqub.Lastfm.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class ScrobblingService : BackgroundService
{
    private readonly ILogger<ScrobblingService> _logger;
    private readonly IOptionsMonitor<ScrobblerConfig> _config;
    private readonly MprisPlayerMonitor _mpris;
    private LastfmClient? _lastfmClient;

    private TrackInfo? _currentTrack;
    private DateTime _lastPollTime;
    private TimeSpan _accumulatedPlayTime;
    private bool _wasPlaying;
    private bool _scrobbled;
    private bool _nowPlayingSent;
    private IDisposable? _configChangeListener;
    private string? _lastApiKey;
    private string? _lastApiSecret;
    private string? _lastSessionKey;

    public bool IsAuthenticated => _lastfmClient?.Session?.Authenticated == true;
    public string? LastScrobbledInfo { get; private set; }
    public TrackInfo? CurrentTrack => _currentTrack;
    public TimeSpan PlayTime => _accumulatedPlayTime;
    public bool Scrobbled => _scrobbled;

    private static readonly string SessionKeyDir = ConfigFileHelper.ConfigDir;
    private static readonly string SessionKeyPath = Path.Combine(SessionKeyDir, "session-key");

    public ScrobblingService(
        ILogger<ScrobblingService> logger,
        IOptionsMonitor<ScrobblerConfig> config,
        MprisPlayerMonitor mpris)
    {
        _logger = logger;
        _config = config;
        _mpris = mpris;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _mpris.ConnectAsync();
            await InitializeLastfmAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize scrobbling service");
            return;
        }

        _lastPollTime = DateTime.UtcNow;
        var cfg = _config.CurrentValue;
        _lastApiKey = cfg.ApiKey;
        _lastApiSecret = cfg.ApiSecret;
        _lastSessionKey = cfg.SessionKey;

        _configChangeListener = _config.OnChange(OnConfigChanged);

        _logger.LogInformation(
            "Scrobbling service started (polling every {Interval}ms, scrobble threshold: {Pct}%)",
            cfg.PollingIntervalMs, cfg.ScrobblePercentage);

        if (!string.IsNullOrEmpty(cfg.PreferredPlayer))
            _logger.LogInformation("Preferred player: {Player}", cfg.PreferredPlayer);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndScrobbleAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during polling cycle");
            }

            await Task.Delay(_config.CurrentValue.PollingIntervalMs, stoppingToken);
        }
    }

    private async Task InitializeLastfmAsync()
    {
        var cfg = _config.CurrentValue;

        var apiKey = cfg.ApiKey;
        var apiSecret = cfg.ApiSecret;

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            throw new InvalidOperationException(
                "Last.fm ApiKey and ApiSecret are required. Run 'scrobbler-cli auth' or set them in appsettings.json.");

        _lastfmClient = new LastfmClient(apiKey, apiSecret);

        // Try saved session key first
        var savedKey = await LoadSessionKeyAsync();
        if (!string.IsNullOrEmpty(savedKey))
        {
            _lastfmClient.Session.SessionKey = savedKey;
            _logger.LogInformation("Loaded saved Last.fm session key");
            return;
        }

        // Try config session key
        if (!string.IsNullOrEmpty(cfg.SessionKey))
        {
            _lastfmClient.Session.SessionKey = cfg.SessionKey;
            await SaveSessionKeyAsync(cfg.SessionKey);
            _logger.LogInformation("Using configured Last.fm session key");
            return;
        }

        // Authenticate with username/password
        if (string.IsNullOrEmpty(cfg.Username) || string.IsNullOrEmpty(cfg.Password))
            throw new InvalidOperationException(
                "Last.fm authentication required. Provide Username/Password or SessionKey in appsettings.json.");

        _logger.LogInformation("Authenticating with Last.fm as '{User}'...", cfg.Username);
        await _lastfmClient.AuthenticateAsync(cfg.Username, cfg.Password);
        await SaveSessionKeyAsync(_lastfmClient.Session.SessionKey);
        _logger.LogInformation("Authenticated successfully, session key saved to {Path}", SessionKeyPath);
    }

    private async Task PollAndScrobbleAsync()
    {
        var now = DateTime.UtcNow;
        var state = await _mpris.GetPlayerStateAsync();

        if (state == null || state.PlaybackStatus != "Playing" || state.Track == null)
        {
            // Player stopped or paused
            if (_wasPlaying && _currentTrack != null)
            {
                await TryScrobbleAsync();
            }
            _wasPlaying = false;
            _lastPollTime = now;
            return;
        }

        var track = state.Track;
        bool trackChanged = HasTrackChanged(track);

        if (trackChanged)
        {
            // Scrobble previous track if eligible
            if (_currentTrack != null)
            {
                await TryScrobbleAsync();
            }

            // Start tracking new track
            _currentTrack = track;
            _accumulatedPlayTime = TimeSpan.Zero;
            _scrobbled = false;
            _nowPlayingSent = false;
            _lastPollTime = now;

            _logger.LogInformation("[{Player}] Now playing: {Artist} - {Title} ({Album})",
                state.PlayerName, track.Artist, track.Title, track.Album);
        }

        // Accumulate play time
        if (_wasPlaying)
        {
            _accumulatedPlayTime += now - _lastPollTime;
        }
        _lastPollTime = now;
        _wasPlaying = true;

        // Send "now playing" update
        if (!_nowPlayingSent)
        {
            await SendNowPlayingAsync(track);
        }

        // Check scrobble threshold
        if (!_scrobbled)
        {
            await TryScrobbleAsync();
        }
    }

    private bool HasTrackChanged(TrackInfo newTrack)
    {
        if (_currentTrack == null) return true;

        // Compare by track ID first (most reliable)
        if (!string.IsNullOrEmpty(_currentTrack.TrackId) && !string.IsNullOrEmpty(newTrack.TrackId))
            return _currentTrack.TrackId != newTrack.TrackId;

        // Fall back to title + artist comparison
        return _currentTrack.Title != newTrack.Title || _currentTrack.Artist != newTrack.Artist;
    }

    private async Task SendNowPlayingAsync(TrackInfo track)
    {
        if (_lastfmClient == null) return;

        try
        {
            await _lastfmClient.Track.UpdateNowPlayingAsync(
                track.Title, track.Artist, album: track.Album);
            _nowPlayingSent = true;
            _logger.LogDebug("Sent 'now playing': {Artist} - {Title}", track.Artist, track.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update now playing");
        }
    }

    private async Task TryScrobbleAsync()
    {
        if (_scrobbled || _currentTrack == null || _lastfmClient == null) return;

        var track = _currentTrack;
        var trackLengthSeconds = track.LengthMicroseconds / 1_000_000.0;

        // Don't scrobble tracks shorter than 30 seconds (Last.fm guideline)
        if (trackLengthSeconds > 0 && trackLengthSeconds < 30)
        {
            _logger.LogDebug("Skipping scrobble for short track ({Length:F0}s): {Artist} - {Title}",
                trackLengthSeconds, track.Artist, track.Title);
            return;
        }

        var playedSeconds = _accumulatedPlayTime.TotalSeconds;

        // Check percentage threshold
        bool thresholdMet;
        if (trackLengthSeconds > 0)
        {
            var percentage = (playedSeconds / trackLengthSeconds) * 100;
            var threshold = _config.CurrentValue.ScrobblePercentage;
            thresholdMet = percentage >= threshold;

            // Also scrobble if played for 4+ minutes (Last.fm guideline)
            if (!thresholdMet && playedSeconds >= 240)
                thresholdMet = true;
        }
        else
        {
            // Unknown track length: scrobble after 4 minutes
            thresholdMet = playedSeconds >= 240;
        }

        if (!thresholdMet) return;

        try
        {
            var scrobble = new Scrobble
            {
                Artist = track.Artist,
                Track = track.Title,
                Album = track.Album,
                Date = DateTime.UtcNow
            };

            var response = await _lastfmClient.Track.ScrobbleAsync(scrobble);
            _scrobbled = true;
            LastScrobbledInfo = $"{track.Artist} - {track.Title}";

            _logger.LogInformation("Scrobbled: {Artist} - {Title} (played {Played:F0}s / {Total:F0}s, accepted: {Accepted})",
                track.Artist, track.Title, playedSeconds, trackLengthSeconds, response.Accepted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scrobble: {Artist} - {Title}", track.Artist, track.Title);
        }
    }

    private static async Task<string?> LoadSessionKeyAsync()
    {
        if (!File.Exists(SessionKeyPath)) return null;
        var content = (await File.ReadAllTextAsync(SessionKeyPath)).Trim();
        return string.IsNullOrEmpty(content) ? null : content;
    }

    private static async Task SaveSessionKeyAsync(string sessionKey)
    {
        Directory.CreateDirectory(SessionKeyDir);
        await File.WriteAllTextAsync(SessionKeyPath, sessionKey);
    }

    public void UpdateSessionKey(string sessionKey)
    {
        if (_lastfmClient == null)
        {
            var cfg = _config.CurrentValue;
            _lastfmClient = new LastfmClient(cfg.ApiKey, cfg.ApiSecret);
        }
        _lastfmClient.Session.SessionKey = sessionKey;
        _lastSessionKey = sessionKey;
        _ = SaveSessionKeyAsync(sessionKey);
        _logger.LogInformation("Session key updated via CLI");
    }

    private void OnConfigChanged(ScrobblerConfig cfg)
    {
        // Credentials changed → reinitialize client
        if (cfg.ApiKey != _lastApiKey || cfg.ApiSecret != _lastApiSecret)
        {
            _logger.LogInformation("API credentials changed, reinitializing Last.fm client");
            _lastApiKey = cfg.ApiKey;
            _lastApiSecret = cfg.ApiSecret;

            if (!string.IsNullOrEmpty(cfg.ApiKey) && !string.IsNullOrEmpty(cfg.ApiSecret))
            {
                _lastfmClient = new LastfmClient(cfg.ApiKey, cfg.ApiSecret);

                // Restore session key
                var sk = cfg.SessionKey;
                if (string.IsNullOrEmpty(sk)) sk = _lastSessionKey;
                if (!string.IsNullOrEmpty(sk))
                    _lastfmClient.Session.SessionKey = sk;
            }
        }
        // Session key changed in config
        else if (cfg.SessionKey != _lastSessionKey && !string.IsNullOrEmpty(cfg.SessionKey))
        {
            _logger.LogInformation("Session key changed in config, updating");
            _lastSessionKey = cfg.SessionKey;
            if (_lastfmClient != null)
                _lastfmClient.Session.SessionKey = cfg.SessionKey;
        }

        if (!string.IsNullOrEmpty(cfg.PreferredPlayer))
            _logger.LogInformation("Preferred player set to: {Player}", cfg.PreferredPlayer);

        _logger.LogInformation(
            "Config reloaded (polling: {Interval}ms, scrobble threshold: {Pct}%)",
            cfg.PollingIntervalMs, cfg.ScrobblePercentage);
    }

    public override void Dispose()
    {
        _configChangeListener?.Dispose();
        base.Dispose();
    }
}

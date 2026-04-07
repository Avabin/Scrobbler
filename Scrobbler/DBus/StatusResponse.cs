namespace Scrobbler.DBus;

internal sealed class StatusResponse
{
    public bool Connected { get; init; }
    public string? SelectedPlayer { get; init; }
    public string[] AllPlayers { get; init; } = [];
    public CurrentlyPlayingStatus? CurrentlyPlaying { get; init; }
    public string? LastScrobbled { get; init; }
    public int ScrobblePercentage { get; init; }
}

internal sealed class CurrentlyPlayingStatus
{
    public string? Artist { get; init; }
    public string? Title { get; init; }
    public string? Album { get; init; }
    public string? Player { get; init; }
    public string? PlaybackStatus { get; init; }
    public long DurationMs { get; init; }
    public long PositionMs { get; init; }
    public long PlayTimeMs { get; init; }
    public bool Scrobbled { get; init; }
}
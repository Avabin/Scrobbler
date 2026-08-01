namespace Scrobbler.LastFm;

using System.Collections.Generic;

/// <summary>Outcome of a single scrobble within a batch (or a standalone scrobble).</summary>
public sealed record ScrobbleResult
{
    /// <summary>The track name as returned by Last.fm (possibly corrected).</summary>
    public string Track { get; init; } = "";

    /// <summary>The artist name as returned by Last.fm (possibly corrected).</summary>
    public string Artist { get; init; } = "";

    /// <summary>The album name as returned by Last.fm (possibly corrected).</summary>
    public string Album { get; init; } = "";

    /// <summary>True if the track name was corrected by the catalogue.</summary>
    public bool TrackCorrected { get; init; }

    /// <summary>True if the artist name was corrected by the catalogue.</summary>
    public bool ArtistCorrected { get; init; }

    /// <summary>True if the album name was corrected by the catalogue.</summary>
    public bool AlbumCorrected { get; init; }

    /// <summary>Ignored message code (0 = accepted, otherwise see docs).</summary>
    public int IgnoredCode { get; init; }

    /// <summary>Human-readable reason when the scrobble was ignored.</summary>
    public string IgnoredMessage { get; init; } = "";

    /// <summary>True when the scrobble was accepted (not ignored).</summary>
    public bool Accepted => IgnoredCode == 0;
}

/// <summary>Result of a <c>track.scrobble</c> request.</summary>
public sealed record ScrobbleResponse
{
    /// <summary>Number of accepted scrobbles.</summary>
    public int Accepted { get; init; }

    /// <summary>Number of ignored scrobbles.</summary>
    public int Ignored { get; init; }

    /// <summary>Per-scrobble results, in submission order.</summary>
    public IReadOnlyList<ScrobbleResult> Results { get; init; } = [];
}

/// <summary>Result of a <c>track.updateNowPlaying</c> request.</summary>
public sealed record NowPlayingResponse
{
    /// <summary>The track name as returned by Last.fm (possibly corrected).</summary>
    public string Track { get; init; } = "";

    /// <summary>The artist name as returned by Last.fm (possibly corrected).</summary>
    public string Artist { get; init; } = "";

    /// <summary>The album name as returned by Last.fm (possibly corrected).</summary>
    public string Album { get; init; } = "";

    /// <summary>Ignored message code (0 = accepted).</summary>
    public int IgnoredCode { get; init; }

    /// <summary>True when the request was accepted (not ignored).</summary>
    public bool Accepted => IgnoredCode == 0;
}

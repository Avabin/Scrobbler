namespace Scrobbler.LastFm;

using System;

/// <summary>
/// A single scrobble to be submitted via <c>track.scrobble</c>.
/// </summary>
public sealed record Scrobble
{
    /// <summary>The artist name. Required.</summary>
    public string Artist { get; init; } = "";

    /// <summary>The track name. Required.</summary>
    public string Track { get; init; } = "";

    /// <summary>The album name. Optional.</summary>
    public string? Album { get; init; }

    /// <summary>The album artist, if it differs from the track artist. Optional.</summary>
    public string? AlbumArtist { get; init; }

    /// <summary>The MusicBrainz Track ID. Optional.</summary>
    public string? MusicBrainzId { get; init; }

    /// <summary>The track number on the album. Optional.</summary>
    public int? TrackNumber { get; init; }

    /// <summary>The length of the track in seconds. Optional.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>
    /// The time the track started playing, in UTC. Required (submitted as a UNIX timestamp).
    /// </summary>
    public DateTime StartedAtUtc { get; init; }

    /// <summary>
    /// Set to <c>true</c> if the user chose this song, <c>false</c> if it was chosen by
    /// someone else (e.g. radio). Omit when there is ambiguity.
    /// </summary>
    public bool? ChosenByUser { get; init; }
}

namespace Scrobbler.LastFm;

using System;

/// <summary>
/// Thrown when the Last.fm API returns an error response (lfm status = "failed").
/// Carries the Last.fm error code so callers can decide whether to retry.
/// </summary>
public sealed class LastfmApiException : Exception
{
    /// <summary>The Last.fm error code (see error code reference).</summary>
    public int Code { get; }

    public LastfmApiException(int code, string message)
        : base(message)
    {
        Code = code;
    }
}

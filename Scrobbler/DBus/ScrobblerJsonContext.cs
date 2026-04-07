namespace Scrobbler.DBus;

using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(CurrentlyPlayingStatus))]
internal partial class ScrobblerJsonContext : JsonSerializerContext
{
}
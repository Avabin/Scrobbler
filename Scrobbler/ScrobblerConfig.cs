namespace Scrobbler;

public class ScrobblerConfig
{
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string SessionKey { get; set; } = "";
    public int ScrobblePercentage { get; set; } = 50;
    public string PreferredPlayer { get; set; } = "";
    public int PollingIntervalMs { get; set; } = 5000;
}

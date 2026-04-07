namespace Scrobbler.Cli.DBus;

using Tmds.DBus.Protocol;

public static class ScrobblerDBusConstants
{
    public const string ServiceName = "org.scrobbler.Daemon";
    public const string ObjectPath = "/org/scrobbler/daemon";
}

internal static class DaemonProxy
{
    public static async Task<Daemon> ConnectAsync()
    {
        var connection = new DBusConnection(DBusAddress.Session!);
        await connection.ConnectAsync();
        return new Daemon(connection, ScrobblerDBusConstants.ServiceName, ScrobblerDBusConstants.ObjectPath);
    }
}

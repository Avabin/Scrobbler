namespace Scrobbler.DBus;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

public class DBusHostingService : BackgroundService
{
    private readonly ILogger<DBusHostingService> _logger;
    private readonly ScrobblerDaemonObject _daemonObject;
    private DBusConnection? _connection;

    public DBusHostingService(ILogger<DBusHostingService> logger, ScrobblerDaemonObject daemonObject)
    {
        _logger = logger;
        _daemonObject = daemonObject;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _connection = new DBusConnection(DBusAddress.Session!);
            await _connection.ConnectAsync();

            _connection.AddMethodHandler(_daemonObject);
            var acquired = await _connection.TryRequestNameAsync(ScrobblerDBusConstants.ServiceName);
            if (!acquired)
            {
                throw new InvalidOperationException($"Failed to acquire D-Bus name '{ScrobblerDBusConstants.ServiceName}'.");
            }

            _logger.LogInformation(
                "D-Bus service registered as {Service} with unique name {UniqueName}",
                ScrobblerDBusConstants.ServiceName,
                _connection.UniqueName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register D-Bus service");
        }
        finally
        {
            _connection?.Dispose();
        }
    }
}

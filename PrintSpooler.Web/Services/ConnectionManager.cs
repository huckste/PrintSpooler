using Microsoft.AspNetCore.SignalR.Client;
using PrintSpooler.Core.Models;

namespace PrintSpooler.Web.Services;

public class ConnectionManager : IAsyncDisposable
{
  private readonly HubConnection _connection;

  public event Action<Job>? JobUpdated;
  public event Action<Printer>? PrinterUpdated;
  public event Action<bool>? ConnectionLost;

  public bool IsConnected => _connection.State == HubConnectionState.Connected;

  public ConnectionManager(IConfiguration config)
  {
    var apiBaseAddress = config["ApiBaseAddress"] ?? throw new InvalidOperationException("ApiBaseAddress is not configured");

    _connection = new HubConnectionBuilder()
      .WithUrl($"{apiBaseAddress}/hubs/updates")
      .WithAutomaticReconnect()
      .Build();

    _connection.On<Job>("JobUpdated", job => JobUpdated?.Invoke(job));
    _connection.On<Printer>("PrinterUpdated", p => PrinterUpdated?.Invoke(p));


    _connection.Reconnecting += async _ => ConnectionLost?.Invoke(true);
    _connection.Reconnected += async _ => ConnectionLost?.Invoke(false);
  }

  public async Task StartAsync()
  {
    if (_connection.State != HubConnectionState.Connected)
      await _connection.StartAsync();
  }

  public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

}

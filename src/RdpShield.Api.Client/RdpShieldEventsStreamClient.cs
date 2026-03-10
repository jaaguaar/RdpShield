using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RdpShield.Api;

namespace RdpShield.Api.Client;

public sealed class RdpShieldEventsStreamClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;

    public RdpShieldEventsStreamClient(string basePipeName = PipeProtocol.BasePipeName, int connectTimeoutMs = 3000)
    {
        _pipeName = basePipeName + ".Events";
        _connectTimeoutMs = connectTimeoutMs;
    }

    /// <summary>
    /// Connects and streams EventDto (JSON lines). Auto-reconnects on disconnect.
    /// </summary>
    public async Task RunAsync(Func<EventDto, Task> onEvent, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(_connectTimeoutMs);
                    await client.ConnectAsync(connectCts.Token);
                }

                using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

                while (!ct.IsCancellationRequested && client.IsConnected)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null) break;

                    EventDto? evt = null;
                    try
                    {
                        evt = JsonSerializer.Deserialize<EventDto>(line, Json);
                    }
                    catch
                    {
                        // ignore malformed line
                    }

                    if (evt is not null)
                        await onEvent(evt);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch
            {
                // reconnect backoff
                await Task.Delay(750, ct);
            }
        }
    }
}

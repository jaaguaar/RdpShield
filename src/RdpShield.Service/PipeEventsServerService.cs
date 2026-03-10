using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RdpShield.Api;
using RdpShield.Service.Live;
using RdpShield.Service.Security;

namespace RdpShield.Service;

public sealed class PipeEventsServerService : BackgroundService
{
    private readonly ILogger<PipeEventsServerService> _logger;
    private readonly LiveEventHub _hub;

    public PipeEventsServerService(
        ILogger<PipeEventsServerService> logger,
        LiveEventHub hub)
    {
        _logger = logger;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pipeName = PipeProtocol.EventsPipeName;
        _logger.LogInformation("Pipe events server starting: {PipeName}", pipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var security = PipeSecurityFactory.Create();

            await using var server = NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.Out, // stream only from service to manager
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 4096,
                security);

            await server.WaitForConnectionAsync(stoppingToken);
            _logger.LogDebug("Events pipe client connected");

            var (id, reader) = _hub.Subscribe();

            try
            {
                using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true)
                {
                    AutoFlush = true
                };

                await foreach (var evt in reader.ReadAllAsync(stoppingToken))
                {
                    var line = JsonSerializer.Serialize(evt, RdpShieldJsonContext.Default.EventDto);
                    await writer.WriteLineAsync(line);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Events pipe IO error");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Events pipe client handler failed");
            }
            finally
            {
                _hub.Unsubscribe(id);
                try { if (server.IsConnected) server.Disconnect(); } catch { }
                _logger.LogDebug("Events pipe client disconnected");
            }
        }
    }
}


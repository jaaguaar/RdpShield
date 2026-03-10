using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RdpShield.Api;

namespace RdpShield.Api.Client;

public sealed class RdpShieldPipeClient : IRdpShieldClient
{
    private static readonly JsonSerializerOptions JsonWire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly RdpShieldPipeClientOptions _opt;

    public RdpShieldPipeClient(RdpShieldPipeClientOptions? options = null)
    {
        _opt = options ?? new RdpShieldPipeClientOptions();
    }

    public Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default)
        => CallAsync<DashboardStatsDto>("GetDashboardStats", null, ct);

    public Task<IReadOnlyList<BanDto>> GetActiveBansAsync(CancellationToken ct = default, int take = 200, int skip = 0)
        => CallAsync<IReadOnlyList<BanDto>>("GetActiveBans",
            new GetActiveBansParams { Take = take, Skip = skip }, ct);

    public Task<IReadOnlyList<AllowlistDto>> GetAllowlistAsync(CancellationToken ct = default, int take = 200, int skip = 0)
        => CallAsync<IReadOnlyList<AllowlistDto>>("GetAllowlist",
            new GetAllowlistParams { Take = take, Skip = skip }, ct);

    public Task AddAllowlistEntryAsync(string entry, string? comment = null, CancellationToken ct = default)
        => CallAsync<object>("AddAllowlistEntry",
            new AddAllowlistParams { Entry = entry, Comment = comment }, ct);

    public Task RemoveAllowlistEntryAsync(string entry, CancellationToken ct = default)
        => CallAsync<object>("RemoveAllowlistEntry",
            new RemoveAllowlistParams { Entry = entry }, ct);

    public Task BlockIpAsync(string ip, string? reason = null, CancellationToken ct = default)
        => CallAsync<object>("BlockIp",
            new BlockIpParams { Ip = ip, Reason = reason }, ct);

    public Task UnbanIpAsync(string ip, CancellationToken ct = default)
        => CallAsync<object>("UnbanIp",
            new UnbanIpParams { Ip = ip }, ct);

    public Task<IReadOnlyList<EventDto>> GetRecentEventsAsync(int take = 20, CancellationToken ct = default, int skip = 0)
        => CallAsync<IReadOnlyList<EventDto>>("GetRecentEvents",
            new GetRecentEventsParams { Take = take, Skip = skip }, ct);

    // -------- Settings v2 (settings.json) --------

    public Task<SettingsDto> GetSettingsAsync(CancellationToken ct = default)
        => CallAsync<SettingsDto>("GetSettings", null, ct);

    public Task UpdateSettingsAsync(SettingsDto settings, CancellationToken ct = default)
        => CallAsync<object>("UpdateSettings", settings, ct); // <-- no wrapper DTO

    // --------------------------------------------

    private async Task<T> CallAsync<T>(string method, object? @params, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(_opt.RequestTimeoutMs);

        var req = new PipeRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Method = method,
            Params = @params
        };

        var resp = await SendAsync(req, linkedCts.Token);

        if (!resp.Ok)
        {
            var code = resp.Error?.Code ?? "unknown";
            var msg = resp.Error?.Message ?? "Unknown error";
            throw new InvalidOperationException($"RdpShield API error [{code}]: {msg}");
        }

        // Methods returning "ok only" (no payload)
        if (typeof(T) == typeof(object) || resp.Result is null)
            return default!;

        // Result comes as JsonElement (because PipeResponse.Result is object)
        if (resp.Result is JsonElement el)
        {
            var typed = el.Deserialize<T>(JsonWire);
            if (typed is null)
                throw new InvalidOperationException("Failed to parse result.");
            return typed;
        }

        // Fallback: serialize then deserialize
        var json = JsonSerializer.Serialize(resp.Result, JsonWire);
        var parsed = JsonSerializer.Deserialize<T>(json, JsonWire);
        if (parsed is null)
            throw new InvalidOperationException("Failed to parse result.");
        return parsed;
    }

    private async Task<PipeResponse> SendAsync(PipeRequest req, CancellationToken ct)
    {
        await using var client = new NamedPipeClientStream(".", _opt.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        // Connect timeout
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(_opt.ConnectTimeoutMs);
            await client.ConnectAsync(connectCts.Token);
        }

        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        var reqJson = JsonSerializer.Serialize(req, JsonWire);
        await writer.WriteLineAsync(reqJson);
        await writer.FlushAsync();

        // ReadLine can hang if server misbehaves; ct cancels this path
        var lineTask = reader.ReadLineAsync();
        var completed = await Task.WhenAny(lineTask, Task.Delay(Timeout.Infinite, ct));
        if (completed != lineTask)
            throw new TimeoutException("Timed out waiting for response.");

        var respLine = await lineTask;
        if (respLine is null)
        {
            return new PipeResponse
            {
                Id = req.Id,
                Ok = false,
                Result = null,
                Error = new PipeError("no_response", "No response from server")
            };
        }

        var resp = JsonSerializer.Deserialize<PipeResponse>(respLine, JsonWire);
        return resp ?? new PipeResponse
        {
            Id = req.Id,
            Ok = false,
            Result = null,
            Error = new PipeError("bad_response", "Failed to parse response")
        };
    }
}

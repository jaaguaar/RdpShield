using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RdpShield.Api;
using RdpShield.Core.Abstractions;
using RdpShield.Service.Security;
using RdpShield.Service.Settings;

namespace RdpShield.Service;

public sealed class PipeServerService : BackgroundService
{
    private readonly ILogger<PipeServerService> _logger;

    private readonly IStatsService _stats;
    private readonly IBanStore _banStore;
    private readonly IAllowlistStore _allowStore;
    private readonly IEventStore _eventStore;
    private readonly IFirewallProvider _firewall;
    private readonly IClock _clock;
    private readonly SettingsStore _settings;

    public PipeServerService(
        ILogger<PipeServerService> logger,
        IStatsService stats,
        IBanStore banStore,
        IAllowlistStore allowStore,
        IEventStore eventStore,
        IFirewallProvider firewall,
        IClock clock,
        SettingsStore settings)
    {
        _logger = logger;
        _stats = stats;
        _banStore = banStore;
        _allowStore = allowStore;
        _eventStore = eventStore;
        _firewall = firewall;
        _clock = clock;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pipe server starting: {PipeName}", PipeProtocol.BasePipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pipeSecurity = PipeSecurityFactory.Create();

            await using var server = NamedPipeServerStreamAcl.Create(
                PipeProtocol.BasePipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 4096,
                outBufferSize: 4096,
                pipeSecurity);

            await server.WaitForConnectionAsync(stoppingToken);
            _logger.LogDebug("Pipe client connected");

            try
            {
                await HandleClientAsync(server, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Pipe IO error");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipe client handler failed");
            }
            finally
            {
                try
                {
                    if (server.IsConnected)
                        server.Disconnect();
                }
                catch { /* ignore */ }

                _logger.LogDebug("Pipe client disconnected");
            }
        }
    }

    private static async Task SendResponseAsync(StreamWriter writer, PipeResponse resp, CancellationToken ct)
    {
        var outJson = JsonSerializer.Serialize(resp, RdpShieldJsonContext.Default.PipeResponse);
        await writer.WriteAsync(outJson + "\n");
        await writer.FlushAsync();
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = false };

        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            PipeRequest? req = null;

            try
            {
                req = JsonSerializer.Deserialize(line, RdpShieldJsonContext.Default.PipeRequest);

                if (req is null)
                {
                    await SendResponseAsync(
                        writer,
                        new PipeResponse
                        {
                            Id = "?",
                            Ok = false,
                            Result = null,
                            Error = new PipeError("bad_request", "Invalid request")
                        },
                        ct);
                    continue;
                }

                var resp = await DispatchAsync(req, ct);
                await SendResponseAsync(writer, resp, ct);
            }
            catch (Exception ex)
            {
                var id = req?.Id ?? "?";
                await SendResponseAsync(
                    writer,
                    new PipeResponse
                    {
                        Id = id,
                        Ok = false,
                        Result = null,
                        Error = new PipeError("exception", ex.Message)
                    },
                    ct);
            }
        }
    }

    private async Task<PipeResponse> DispatchAsync(PipeRequest req, CancellationToken ct)
    {
        switch (req.Method)
        {
            case "GetDashboardStats":
            {
                var s = await _stats.GetDashboardStatsAsync(ct);
                var dto = new DashboardStatsDto
                {
                    ActiveBansCount = s.ActiveBansCount,
                    FailedAttemptsLast10m = s.FailedAttemptsLast10m,
                    LastBannedIp = s.LastBannedIp,
                    LastBannedAtUtc = s.LastBannedAtUtc
                };

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(dto, RdpShieldJsonContext.Default.DashboardStatsDto),
                    Error = null
                };
            }

            case "GetActiveBans":
            {
                var p = req.Params is null ? new GetActiveBansParams() : GetParams<GetActiveBansParams>(req);
                var take = Math.Clamp(p.Take, 1, 1000);
                var skip = Math.Max(0, p.Skip);

                var bans = await _banStore.GetActiveBansAsync(ct);
                var dto = bans.Select(b => new BanDto
                {
                    Ip = b.Ip,
                    Reason = b.Reason,
                    Source = b.Source,
                    FirstSeenUtc = b.FirstSeenUtc,
                    LastSeenUtc = b.LastSeenUtc,
                    ExpiresUtc = b.ExpiresUtc,
                    AttemptsInWindow = b.AttemptsInWindow
                })
                .OrderByDescending(x => x.ExpiresUtc)
                .Skip(skip)
                .Take(take)
                .ToList();

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(dto, RdpShieldJsonContext.Default.ListBanDto),
                    Error = null
                };
            }

            case "UnbanIp":
            {
                var p = GetParams<UnbanIpParams>(req);
                var ip = p.Ip;

                var s = _settings.Current;
                var ruleName = $"{s.FirewallRulePrefix} {ip}";

                try
                {
                    if (s.EnableFirewall)
                        await _firewall.UnbanIpAsync(ip, ruleName, ct);
                }
                catch (Exception ex)
                {
                    await _eventStore.AppendAsync(
                        _clock.UtcNow, "Error", "FirewallError",
                        $"Failed to unban {ip} in firewall: {ex.Message}",
                        ip: ip, ct: ct);
                }

                await _banStore.MarkUnbannedAsync(ip, _clock.UtcNow, ct);
                await _eventStore.AppendAsync(
                    _clock.UtcNow, "Information", "IpUnbanned",
                    $"Unbanned {ip} (manual)", ip: ip, ct: ct);

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(true, RdpShieldJsonContext.Default.Boolean),
                    Error = null
                };
            }

            case "BlockIp":
            {
                var p = GetParams<BlockIpParams>(req);
                var ip = p.Ip?.Trim() ?? string.Empty;
                if (!IPAddress.TryParse(ip, out _))
                    throw new InvalidOperationException("Invalid IP address.");
                var reason = string.IsNullOrWhiteSpace(p.Reason)
                    ? "Manual block"
                    : p.Reason.Trim();

                var now = _clock.UtcNow;
                var s = _settings.Current;
                var expires = now.AddMinutes(s.BanMinutes);
                var ruleName = $"{s.FirewallRulePrefix} {ip}";

                var ban = new RdpShield.Core.Models.BanRecord(
                    ip,
                    reason,
                    "Manual",
                    now,
                    now,
                    expires,
                    1);

                await _banStore.UpsertBanAsync(ban, ct);

                try
                {
                    if (s.EnableFirewall)
                    {
                        await _firewall.BanIpAsync(
                            ip,
                            ruleName,
                            3389,
                            $"RdpShield manual ban until {expires:O}",
                            ct);
                    }
                }
                catch (Exception ex)
                {
                    await _eventStore.AppendAsync(
                        now, "Error", "FirewallError",
                        $"Failed to ban {ip} in firewall: {ex.Message}",
                        ip: ip, ct: ct);
                }

                await _eventStore.AppendAsync(
                    now, "Warning", "IpBanned",
                    $"Banned {ip}: {reason}",
                    ip: ip, source: "Manual", ct: ct);

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(true, RdpShieldJsonContext.Default.Boolean),
                    Error = null
                };
            }

            case "GetAllowlist":
            {
                var p = req.Params is null ? new GetAllowlistParams() : GetParams<GetAllowlistParams>(req);
                var take = Math.Clamp(p.Take, 1, 1000);
                var skip = Math.Max(0, p.Skip);

                var list = await _allowStore.GetAllAsync(ct);
                var dto = list.Select(x => new AllowlistDto
                {
                    Entry = x.Entry,
                    Comment = x.Comment
                })
                .OrderBy(x => x.Entry, StringComparer.OrdinalIgnoreCase)
                .Skip(skip)
                .Take(take)
                .ToList();

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(dto, RdpShieldJsonContext.Default.ListAllowlistDto),
                    Error = null
                };
            }

            case "AddAllowlistEntry":
            {
                var p = GetParams<AddAllowlistParams>(req);
                await _allowStore.AddOrUpdateAsync(p.Entry, p.Comment, ct);
                await _eventStore.AppendAsync(
                    _clock.UtcNow, "Information", "AllowlistUpdated",
                    $"Allowlist add/update: {p.Entry}", ct: ct);

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(true, RdpShieldJsonContext.Default.Boolean),
                    Error = null
                };
            }

            case "RemoveAllowlistEntry":
            {
                var p = GetParams<RemoveAllowlistParams>(req);
                await _allowStore.RemoveAsync(p.Entry, ct);
                await _eventStore.AppendAsync(
                    _clock.UtcNow, "Information", "AllowlistUpdated",
                    $"Allowlist removed: {p.Entry}", ct: ct);

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(true, RdpShieldJsonContext.Default.Boolean),
                    Error = null
                };
            }

            case "GetRecentEvents":
            {
                var p = GetParams<GetRecentEventsParams>(req);
                var take = RecentEventsTake.Normalize(p.Take);
                var skip = Math.Max(0, p.Skip);
                var rows = await _eventStore.GetLatestAsync(take, ct, skip);

                var dto = rows.Select(r => new EventDto
                {
                    TsUtc = r.tsUtc,
                    Level = r.level,
                    Type = r.type,
                    Message = r.message,
                    Username = TryExtractUsername(r.payloadJson),
                    Ip = r.ip,
                    Source = r.source
                }).ToList();

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(dto, RdpShieldJsonContext.Default.ListEventDto),
                    Error = null
                };
            }

            // -------- Settings (settings.json) --------

            case "GetSettings":
            {
                var s = _settings.Current;
                var dto = new SettingsDto
                {
                    AttemptsThreshold = s.AttemptsThreshold,
                    WindowSeconds = s.WindowSeconds,
                    BanMinutes = s.BanMinutes,
                    EnableFirewall = s.EnableFirewall,
                    FirewallRulePrefix = s.FirewallRulePrefix,
                    AllowlistRefreshSeconds = s.AllowlistRefreshSeconds
                };

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(dto, RdpShieldJsonContext.Default.SettingsDto),
                    Error = null
                };
            }

            case "UpdateSettings":
            {
                var dto = GetParams<SettingsDto>(req);

                var s = _settings.Current;
                s.AttemptsThreshold = dto.AttemptsThreshold;
                s.WindowSeconds = dto.WindowSeconds;
                s.BanMinutes = dto.BanMinutes;

                s.EnableFirewall = dto.EnableFirewall;

                s.FirewallRulePrefix = string.IsNullOrWhiteSpace(dto.FirewallRulePrefix)
                    ? "RdpShield Block"
                    : dto.FirewallRulePrefix;

                s.AllowlistRefreshSeconds = dto.AllowlistRefreshSeconds;

                _settings.Update(s);

                await _eventStore.AppendAsync(
                    _clock.UtcNow,
                    "Information",
                    "SettingsUpdated",
                    $"Settings updated: attempts={dto.AttemptsThreshold}, window={dto.WindowSeconds}s, ban={dto.BanMinutes}m, firewall={dto.EnableFirewall}, allowRefresh={dto.AllowlistRefreshSeconds}s",
                    ct: ct);

                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = true,
                    Result = ToJsonElement(true, RdpShieldJsonContext.Default.Boolean),
                    Error = null
                };
            }

            default:
                return new PipeResponse
                {
                    Id = req.Id,
                    Ok = false,
                    Result = null,
                    Error = new PipeError("unknown_method", req.Method)
                };
        }
    }

    private static T GetParams<T>(PipeRequest req)
    {
        if (req.Params is null)
            throw new InvalidOperationException("Missing params");

        if (req.Params is not JsonElement el)
            throw new InvalidOperationException("Invalid params payload.");

        return JsonSerializer.Deserialize(el, GetTypeInfo<T>())!;
    }

    private static JsonTypeInfo<T> GetTypeInfo<T>()
    {
        var type = typeof(T);

        if (type == typeof(UnbanIpParams))
            return (JsonTypeInfo<T>)(object)RdpShieldJsonContext.Default.UnbanIpParams;
        if (type == typeof(BlockIpParams))
            return (JsonTypeInfo<T>)(object)RdpShieldJsonContext.Default.BlockIpParams;
        if (type == typeof(GetActiveBansParams))
            return (JsonTypeInfo<T>)(object)RdpShieldJsonContext.Default.GetActiveBansParams;
        if (type == typeof(GetAllowlistParams))
            return (JsonTypeInfo<T>)(object)RdpShieldJsonContext.Default.GetAllowlistParams;
        if (type == typeof(AddAllowlistParams))
            return (JsonTypeInfo<T>)(object)RdpShieldJsonContext.Default.AddAllowlistParams;
        if (type == typeof(RemoveAllowlistParams))
            return (JsonTypeInfo<T>)(object)RdpShieldJsonContext.Default.RemoveAllowlistParams;
        if (type == typeof(GetRecentEventsParams))
            return (JsonTypeInfo<T>)(object)RdpShieldJsonContext.Default.GetRecentEventsParams;
        if (type == typeof(SettingsDto))
            return (JsonTypeInfo<T>)(object)RdpShieldJsonContext.Default.SettingsDto;

        throw new NotSupportedException($"No JSON type info registered for {type.FullName}.");
    }

    private static JsonElement ToJsonElement<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.SerializeToElement(value, typeInfo);

    private static string? TryExtractUsername(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String)
                return un.GetString();
        }
        catch
        {
            // Ignore malformed payload.
        }

        return null;
    }
}

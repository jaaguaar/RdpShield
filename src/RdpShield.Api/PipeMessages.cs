namespace RdpShield.Api;

public sealed class PipeRequest
{
    public string Id { get; set; } = "";
    public string Method { get; set; } = "";
    public object? Params { get; set; }
}

public sealed class PipeResponse
{
    public string Id { get; set; } = "";
    public bool Ok { get; set; }
    public object? Result { get; set; }
    public PipeError? Error { get; set; }
}

public sealed class PipeError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";

    public PipeError() { }
    public PipeError(string code, string message)
    {
        Code = code;
        Message = message;
    }
}

// Params DTOs
public sealed class UnbanIpParams { public string Ip { get; set; } = ""; }
public sealed class BlockIpParams
{
    public string Ip { get; set; } = "";
    public string? Reason { get; set; }
}
public sealed class AddAllowlistParams { public string Entry { get; set; } = ""; public string? Comment { get; set; } }
public sealed class RemoveAllowlistParams { public string Entry { get; set; } = ""; }
public sealed class GetRecentEventsParams
{
    public int Take { get; set; } = 50;
    public int Skip { get; set; }
}
public sealed class GetActiveBansParams
{
    public int Take { get; set; } = 200;
    public int Skip { get; set; }
}
public sealed class GetAllowlistParams
{
    public int Take { get; set; } = 200;
    public int Skip { get; set; }
}

// Result DTOs
public sealed class BanDto
{
    public string Ip { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public int AttemptsInWindow { get; set; }
}

public sealed class EventDto
{
    public DateTimeOffset TsUtc { get; set; }
    public string Level { get; set; } = "";
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Username { get; set; }
    public string? Ip { get; set; }
    public string? Source { get; set; }
}

public sealed class AllowlistDto
{
    public string Entry { get; set; } = "";
    public string? Comment { get; set; }
}

public sealed class DashboardStatsDto
{
    public int ActiveBansCount { get; set; }
    public int FailedAttemptsLast10m { get; set; }
    public string? LastBannedIp { get; set; }
    public DateTimeOffset? LastBannedAtUtc { get; set; }
}

// Settings DTO (single source of truth)
public sealed class SettingsDto
{
    public int AttemptsThreshold { get; set; }
    public int WindowSeconds { get; set; }
    public int BanMinutes { get; set; }

    public bool EnableFirewall { get; set; }
    public string FirewallRulePrefix { get; set; } = "RdpShield Block";

    public int AllowlistRefreshSeconds { get; set; }
}

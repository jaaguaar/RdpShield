using System.Text.Json;
using System.Text.Json.Serialization;
using RdpShield.Api;
using RdpShield.Service.Settings;

namespace RdpShield.Service;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(PipeRequest))]
[JsonSerializable(typeof(PipeResponse))]
[JsonSerializable(typeof(PipeError))]
[JsonSerializable(typeof(UnbanIpParams))]
[JsonSerializable(typeof(BlockIpParams))]
[JsonSerializable(typeof(GetActiveBansParams))]
[JsonSerializable(typeof(GetAllowlistParams))]
[JsonSerializable(typeof(AddAllowlistParams))]
[JsonSerializable(typeof(RemoveAllowlistParams))]
[JsonSerializable(typeof(GetRecentEventsParams))]
[JsonSerializable(typeof(DashboardStatsDto))]
[JsonSerializable(typeof(BanDto))]
[JsonSerializable(typeof(List<BanDto>))]
[JsonSerializable(typeof(EventDto))]
[JsonSerializable(typeof(List<EventDto>))]
[JsonSerializable(typeof(AllowlistDto))]
[JsonSerializable(typeof(List<AllowlistDto>))]
[JsonSerializable(typeof(SettingsDto))]
[JsonSerializable(typeof(RuntimeSettings))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class RdpShieldJsonContext : JsonSerializerContext
{
}

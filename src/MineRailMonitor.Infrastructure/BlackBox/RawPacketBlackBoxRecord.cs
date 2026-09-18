using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.BlackBox;

public sealed class RawPacketBlackBoxRecord
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("time")]
    public DateTimeOffset Time { get; set; }

    [JsonProperty("direction")]
    public string Direction { get; set; } = string.Empty;

    [JsonProperty("yard")]
    public string YardId { get; set; } = string.Empty;

    [JsonProperty("stationId", NullValueHandling = NullValueHandling.Include)]
    public string? StationId { get; set; }

    [JsonProperty("local", NullValueHandling = NullValueHandling.Include)]
    public string? LocalEndPoint { get; set; }

    [JsonProperty("remote", NullValueHandling = NullValueHandling.Include)]
    public string? RemoteEndPoint { get; set; }

    [JsonProperty("protocolAddress", NullValueHandling = NullValueHandling.Include)]
    public string? ProtocolAddress { get; set; }

    [JsonProperty("length")]
    public int Length { get; set; }

    [JsonProperty("valid", NullValueHandling = NullValueHandling.Include)]
    public bool? Valid { get; set; }

    [JsonProperty("validationError", NullValueHandling = NullValueHandling.Include)]
    public string? ValidationError { get; set; }

    [JsonProperty("command", NullValueHandling = NullValueHandling.Include)]
    public string? Command { get; set; }

    [JsonProperty("hex")]
    public string Hex { get; set; } = string.Empty;
}

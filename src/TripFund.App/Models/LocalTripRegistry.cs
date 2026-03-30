using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class LocalTripRegistry
{
    [JsonPropertyName("trips")]
    public Dictionary<string, TripRegistryEntry> Trips { get; set; } = new();
}

public class TripRegistryEntry
{
    [JsonPropertyName("driveFolderId")]
    public string DriveFolderId { get; set; } = string.Empty;
}

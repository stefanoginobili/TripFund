using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class LocalTripRegistry
{
    [JsonPropertyName("trips")]
    public Dictionary<string, TripRegistryEntry> Trips { get; set; } = new();
}

public class TripRegistryEntry
{
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("sync")]
    public SyncConfig Sync { get; set; } = new();
}

public class SyncConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();

    [JsonPropertyName("lastSync")]
    public DateTime? LastSync { get; set; }

    [JsonPropertyName("hasConflicts")]
    public bool HasConflicts { get; set; }
}

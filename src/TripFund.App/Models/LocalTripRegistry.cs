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

    [JsonPropertyName("remoteStorage")]
    public RemoteStorageConfig? RemoteStorage { get; set; }
}

public class RemoteStorageConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("remoteUniqueId")]
    public string RemoteUniqueId { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();

    [JsonPropertyName("lastSynchronized")]
    public DateTime? LastSynchronized { get; set; }

    [JsonPropertyName("hasConflicts")]
    public bool HasConflicts { get; set; }

    [JsonPropertyName("readonly")]
    public bool Readonly { get; set; }
}

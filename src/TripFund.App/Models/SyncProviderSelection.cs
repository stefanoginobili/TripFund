using System.Collections.Generic;

namespace TripFund.App.Models;

public class SyncProviderSelection
{
    public string Provider { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}

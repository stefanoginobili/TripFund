using System.Collections.Generic;

namespace TripFund.App.Models;

public class RemoteStorageSelection
{
    public string Provider { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
    public bool IsReadonly { get; set; }
}

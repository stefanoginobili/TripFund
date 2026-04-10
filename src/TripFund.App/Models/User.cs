using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class User
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;
}

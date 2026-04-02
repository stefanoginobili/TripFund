using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class AppSettings
{
    [JsonPropertyName("authorName")]
    public string AuthorName { get; set; } = string.Empty;

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;
}

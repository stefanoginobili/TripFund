using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class AppSettings
{
    [JsonPropertyName("authorName")]
    public string AuthorName { get; set; } = string.Empty;

    [JsonPropertyName("authorSlug")]
    public string AuthorSlug { get; set; } = string.Empty;
}

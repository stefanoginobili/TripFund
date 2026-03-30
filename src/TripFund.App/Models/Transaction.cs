using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class Transaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "expense" or "contribution"

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("split")]
    public Dictionary<string, decimal> Split { get; set; } = new();

    [JsonPropertyName("location")]
    public LocationInfo? Location { get; set; }

    [JsonPropertyName("attachments")]
    public List<string> Attachments { get; set; } = new();
}

public class LocationInfo
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

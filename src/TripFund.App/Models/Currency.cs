using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class Currency
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("decimals")]
    public int Decimals { get; set; } = 2;

    [JsonPropertyName("expectedQuotaPerMember")]
    public decimal ExpectedQuotaPerMember { get; set; }
}

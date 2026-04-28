using System.Text.Json.Serialization;

namespace TripFund.App.Models;

public class TripConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("startDate")]
    [JsonConverter(typeof(TripFund.App.Utilities.DateOnlyJsonConverter))]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("endDate")]
    [JsonConverter(typeof(TripFund.App.Utilities.DateOnlyJsonConverter))]
    public DateTime EndDate { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("currencies")]
    public Dictionary<string, Currency> Currencies { get; set; } = new();

    [JsonPropertyName("members")]
    public Dictionary<string, User> Members { get; set; } = new();

    [JsonPropertyName("categories")]
    public TripCategories Categories { get; set; } = new();
}

public class TripCategories
{
    [JsonPropertyName("expenses")]
    public Dictionary<string, ExpenseCategory> Expenses { get; set; } = new();
}

public class ExpenseCategory
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;
}

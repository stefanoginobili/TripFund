namespace TripFund.App.Models;

public class ConflictInfo
{
    public string Id { get; set; } = string.Empty; // "config" or transactionId
    public string Type { get; set; } = string.Empty; // "config", "expense", "contribution"
    public string Label { get; set; } = string.Empty;
}

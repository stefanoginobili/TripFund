namespace TripFund.App.Models;

public class Currency
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ExpectedQuotaPerMember { get; set; }
}

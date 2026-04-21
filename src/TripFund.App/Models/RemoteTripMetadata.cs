namespace TripFund.App.Models;

public class RemoteTripMetadata
{
    public bool IsValid { get; set; }
    public string TripSlug { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

namespace TripFund.App.Utilities;

public static class TransactionIdGenerator
{
    public static string GenerateId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var guidPrefix = Guid.NewGuid().ToString("n").Substring(0, 8);
        return $"{timestamp}-{guidPrefix}";
    }
}

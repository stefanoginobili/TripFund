namespace TripFund.App.Models
{
    public class MemberStats
    {
        public string Slug { get; set; } = "";
        public string Name { get; set; } = "";
        public string Avatar { get; set; } = "";
        public decimal TotalContributed { get; set; }
        public decimal RemainingBalance { get; set; }
        public bool IsMissing { get; set; }
    }
}

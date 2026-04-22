using Microsoft.AspNetCore.Components;
using TripFund.App.Models;

namespace TripFund.App.Components.Common
{
    public partial class MembersModal
    {
        [Parameter] public bool IsVisible { get; set; }
        [Parameter] public List<MemberStats>? MembersStats { get; set; }
        [Parameter] public string TripSlug { get; set; } = "";
        [Parameter] public string SelectedCurrency { get; set; } = "";
        [Parameter] public TripConfig? Config { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }
        [Parameter] public EventCallback<string> OnMemberSelected { get; set; }

        private async Task Close() => await OnClose.InvokeAsync();

        private async Task SelectMember(string slug)
        {
            await OnMemberSelected.InvokeAsync(slug);
        }

        private string FormatCurrency(decimal amount)
        {
            if (Config != null && Config.Currencies.TryGetValue(SelectedCurrency, out var c))
            {
                string format = c.Decimals > 0 ? "N" + c.Decimals : "N0";
                return $"{c.Symbol} {amount.ToString(format)}";
            }
            return $"{amount:N2} {SelectedCurrency}";
        }
    }
}

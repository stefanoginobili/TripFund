using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Linq;

namespace TripFund.App.Components.Common
{
    public partial class CurrencySelector
    {
        [Parameter] public IEnumerable<string> Currencies { get; set; } = Enumerable.Empty<string>();
        [Parameter] public string SelectedCurrency { get; set; } = "";
        [Parameter] public EventCallback<string> OnCurrencySelected { get; set; }
    }
}

using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.App.Components.Common
{
    public partial class CurrenciesForm
    {
        [Inject] private IAlertService Alerts { get; set; } = default!;

        [Parameter] public Dictionary<string, Currency> Currencies { get; set; } = new();
        [Parameter] public EventCallback<Dictionary<string, Currency>> CurrenciesChanged { get; set; }
        [Parameter] public bool OpenNewCurrencyOnOpen { get; set; } = false;

        private string error = "";
        private string newCurrCode = "";
        private string newCurrSymbol = "";
        private decimal newCurrQuota = 0;
        private int newCurrDecimals = 2;
        private bool isAddingCurrency = false;
        private string? openMenuCurrencyCode = null;
        private string? editingCurrencyCode = null;

        protected override void OnInitialized()
        {
            if (OpenNewCurrencyOnOpen || Currencies == null || Currencies.Count == 0)
            {
                isAddingCurrency = true;
            }
        }

        private async Task AddOrUpdateCurrency()
        {
            newCurrCode = newCurrCode?.Trim() ?? "";
            newCurrSymbol = newCurrSymbol?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(newCurrCode)) return;
            string code = newCurrCode.ToUpperInvariant();

            if (editingCurrencyCode == null && Currencies.ContainsKey(code))
            {
                error = "Valuta già presente.";
                return;
            }

            Currencies[code] = new Currency
            {
                Symbol = string.IsNullOrWhiteSpace(newCurrSymbol) ? code : newCurrSymbol,
                Name = code,
                Decimals = newCurrDecimals,
                ExpectedQuotaPerMember = newCurrQuota
            };

            newCurrCode = "";
            newCurrSymbol = "";
            newCurrQuota = 0;
            newCurrDecimals = 2;
            error = "";
            isAddingCurrency = false;
            editingCurrencyCode = null;
            await CurrenciesChanged.InvokeAsync(Currencies);
        }

        private void OnDecimalsChanged(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out int val))
            {
                newCurrDecimals = System.Math.Clamp(val, 0, 4);
            }
        }

        private void TrimCode()
        {
            newCurrCode = newCurrCode?.Trim() ?? "";
        }

        private void TrimSymbol()
        {
            newCurrSymbol = newCurrSymbol?.Trim() ?? "";
        }

        private void OnQuotaChanged(ChangeEventArgs e)
        {
            var input = e.Value?.ToString()?.Replace(".", ",");
            if (decimal.TryParse(input, out decimal val))
            {
                newCurrQuota = val;
            }
        }

        private void ToggleCurrencyMenu(string code)
        {
            if (openMenuCurrencyCode == code) openMenuCurrencyCode = null;
            else openMenuCurrencyCode = code;
        }

        private void StartEditCurrency(string code, Currency c)
        {
            isAddingCurrency = false;
            editingCurrencyCode = code;
            newCurrCode = code;
            newCurrSymbol = c.Symbol;
            newCurrDecimals = c.Decimals;
            newCurrQuota = c.ExpectedQuotaPerMember;
            openMenuCurrencyCode = null;
        }

        private void CancelCurrencyEdit()
        {
            editingCurrencyCode = null;
            newCurrCode = "";
            newCurrSymbol = "";
            newCurrDecimals = 2;
            newCurrQuota = 0;
        }

        private async Task MoveCurrencyUp(string code)
        {
            var keys = Currencies.Keys.ToList();
            int index = keys.IndexOf(code);
            if (index <= 0) return;

            var prevKey = keys[index - 1];
            var currentVal = Currencies[code];
            var prevVal = Currencies[prevKey];

            var newDict = new Dictionary<string, Currency>();
            foreach (var key in keys)
            {
                if (key == prevKey) newDict[code] = currentVal;
                else if (key == code) newDict[prevKey] = prevVal;
                else newDict[key] = Currencies[key];
            }
            Currencies = newDict;
            openMenuCurrencyCode = null;
            await CurrenciesChanged.InvokeAsync(Currencies);
        }

        private async Task MoveCurrencyDown(string code)
        {
            var keys = Currencies.Keys.ToList();
            int index = keys.IndexOf(code);
            if (index < 0 || index >= keys.Count - 1) return;

            var nextKey = keys[index + 1];
            var currentVal = Currencies[code];
            var nextVal = Currencies[nextKey];

            var newDict = new Dictionary<string, Currency>();
            foreach (var key in keys)
            {
                if (key == code) newDict[nextKey] = nextVal;
                else if (key == nextKey) newDict[code] = currentVal;
                else newDict[key] = Currencies[key];
            }

            Currencies = newDict;
            openMenuCurrencyCode = null;
            await CurrenciesChanged.InvokeAsync(Currencies);
        }

        private async Task ConfirmDeleteCurrency(string code)
        {
            openMenuCurrencyCode = null;
            bool confirm = await Alerts.ConfirmAsync(
                "Elimina Valuta",
                $"Sei sicuro di voler eliminare la cassa in {code}? Tutte le transazioni in questa valuta potrebbero non essere più visualizzate correttamente.",
                "Elimina",
                "Annulla");

            if (confirm)
            {
                Currencies.Remove(code);
                await CurrenciesChanged.InvokeAsync(Currencies);
            }
        }

        private string FormatCurrencyValue(decimal amount, Currency c)
        {
            string format = c.Decimals > 0 ? "N" + c.Decimals : "N0";
            return $"{c.Symbol} {amount.ToString(format, new System.Globalization.CultureInfo("it-IT"))}";
        }
    }
}

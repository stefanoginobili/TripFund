using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Constants;

namespace TripFund.App.Components.Common
{
    public partial class CurrenciesForm
    {
        [Inject] private IAlertService Alerts { get; set; } = default!;

        [Parameter] public Dictionary<string, Currency> Currencies { get; set; } = new();
        [Parameter] public EventCallback<Dictionary<string, Currency>> CurrenciesChanged { get; set; }
        [Parameter] public bool OpenNewCurrencyOnOpen { get; set; } = false;
        [Parameter] public bool IsReadonly { get; set; } = false;

        private string error = "";
        private string newCurrCode = "";
        private string newCurrSymbol = "";
        private string newCurrQuotaString = "0";
        private decimal newCurrQuota = 0;
        private int newCurrDecimals = 2;
        private bool isAddingCurrency = false;
        private string? editingCurrencyCode = null;

        private List<IsoCurrencyInfo> filteredCurrencies = new();
        private bool showSuggestions = false;

        protected override void OnInitialized()
        {
            if (OpenNewCurrencyOnOpen || Currencies == null || Currencies.Count == 0)
            {
                isAddingCurrency = true;
                newCurrQuotaString = newCurrQuota.ToString("F" + newCurrDecimals);
            }
        }

        private async Task AddOrUpdateCurrency()
        {
            newCurrCode = newCurrCode?.Trim() ?? "";
            newCurrSymbol = newCurrSymbol?.Trim() ?? "";

            // Ensure the quota is parsed from the current input string
            ParseQuota(newCurrQuotaString);

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
            newCurrQuotaString = newCurrQuota.ToString("F" + newCurrDecimals);
            error = "";
            isAddingCurrency = false;
            editingCurrencyCode = null;
            showSuggestions = false;
            await CurrenciesChanged.InvokeAsync(Currencies);
        }

        private void OnDecimalsChanged(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out int val))
            {
                newCurrDecimals = System.Math.Clamp(val, 0, 4);
            }
        }

        private void OnCodeInput(ChangeEventArgs e)
        {
            newCurrCode = e.Value?.ToString() ?? "";
            UpdateSuggestions();
        }

        private void OnCodeFocus()
        {
            UpdateSuggestions();
        }

        private async Task OnCodeBlur()
        {
            // Delay to allow @onmousedown on suggestion items to fire
            await Task.Delay(200);
            showSuggestions = false;
            StateHasChanged();
        }

        private void UpdateSuggestions()
        {
            if (string.IsNullOrWhiteSpace(newCurrCode))
            {
                filteredCurrencies.Clear();
                showSuggestions = false;
                return;
            }

            var query = newCurrCode.Trim().ToLowerInvariant();
            
            // Match on code (StartsWith, CI) and on the name (Contains, CI)
            // Matches on currency code must be shown first
            var codeMatches = IsoCurrencies.All
                .Where(c => c.Code.ToLowerInvariant().StartsWith(query))
                .OrderBy(c => c.Code)
                .ToList();

            var nameMatches = IsoCurrencies.All
                .Where(c => !c.Code.ToLowerInvariant().StartsWith(query) && c.Name.ToLowerInvariant().Contains(query))
                .OrderBy(c => c.Name)
                .ToList();

            filteredCurrencies = codeMatches.Concat(nameMatches).Take(10).ToList();
            showSuggestions = filteredCurrencies.Any();
        }

        private void SelectSuggestion(IsoCurrencyInfo info)
        {
            newCurrCode = info.Code;
            newCurrSymbol = info.Symbol;
            newCurrDecimals = info.Decimals;
            showSuggestions = false;
            filteredCurrencies.Clear();
            StateHasChanged();
        }

        private MarkupString HighlightMatch(string text, string match)
        {
            if (string.IsNullOrWhiteSpace(match)) return (MarkupString)text;

            var index = text.IndexOf(match, System.StringComparison.OrdinalIgnoreCase);
            if (index == -1) return (MarkupString)text;

            var before = text.Substring(0, index);
            var matched = text.Substring(index, match.Length);
            var after = text.Substring(index + match.Length);

            return (MarkupString)$"{before}<b>{matched}</b>{after}";
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
            newCurrQuotaString = e.Value?.ToString() ?? "";
            ParseQuota(newCurrQuotaString);
        }

        private void ParseQuota(string input)
        {
            var cleanInput = input?.Replace(".", ",") ?? "";
            if (decimal.TryParse(cleanInput, out decimal val))
            {
                newCurrQuota = val;
            }
        }

        private void StartEditCurrency(string code, Currency c)
        {
            isAddingCurrency = false;
            editingCurrencyCode = code;
            newCurrCode = code;
            newCurrSymbol = c.Symbol;
            newCurrDecimals = c.Decimals;
            newCurrQuota = c.ExpectedQuotaPerMember;
            newCurrQuotaString = newCurrQuota.ToString("F" + newCurrDecimals);
        }

        private void CancelCurrencyEdit()
        {
            editingCurrencyCode = null;
            newCurrCode = "";
            newCurrSymbol = "";
            newCurrDecimals = 2;
            newCurrQuota = 0;
            newCurrQuotaString = newCurrQuota.ToString("F" + newCurrDecimals);
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
            await CurrenciesChanged.InvokeAsync(Currencies);
        }

        private async Task ConfirmDeleteCurrency(string code)
        {
            bool confirm = await Alerts.ConfirmAsync(
                "Elimina Cassa",
                $"Sei sicuro di voler eliminare la cassa in <b>{code}</b>?<br/><br/>Tutte le transazioni in questa valuta potrebbero non essere più visualizzate correttamente.",
                "Elimina",
                "Annulla",
                AlertType.Warning);

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

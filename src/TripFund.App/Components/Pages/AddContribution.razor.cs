using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class AddContribution
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private IEmailService EmailService { get; set; } = default!;
        [Inject] private IAlertService AlertService { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";
        
        [SupplyParameterFromQuery] public string? member { get; set; }
        [SupplyParameterFromQuery] public string? currency { get; set; }
        [SupplyParameterFromQuery] public string? edit { get; set; }

        private TripConfig? config;
        private Transaction? editingTransaction;
        private string? originalTxJson;
        private List<Transaction> allTransactions = new();
        private string selectedCurrency = "";
        private string selectedMemberSlug = "";
        private decimal amount;
        private bool isAmountDirty = false;
        private string description = "Versamento in cassa";
        private DateTime transactionDate = DateTime.Now;
        private string timezoneId = TimeZoneInfo.Local.Id;
        private string errorMessage = "";
        private string deviceId = "";
        private string authorName = "";
        private bool isSubmitting = false;
        private bool isMemberSelectorOpen = false;
        private bool isMenuOpen = false;
        private bool shouldScroll = false;

        protected override async Task OnInitializedAsync()
        {
            config = await Storage.GetTripConfigAsync(tripSlug);
            allTransactions = await Storage.GetTransactionsAsync(tripSlug) ?? new();
            var settings = await Storage.GetAppSettingsAsync();
            deviceId = settings?.DeviceId ?? "unknown";
            authorName = settings?.AuthorName ?? "Unknown";

            if (config != null)
            {
                if (!string.IsNullOrEmpty(edit))
                {
                    editingTransaction = await Storage.GetLatestTransactionVersionAsync(tripSlug, edit);
                    if (editingTransaction != null && editingTransaction.Type == "contribution")
                    {
                        originalTxJson = System.Text.Json.JsonSerializer.Serialize(editingTransaction);
                        selectedCurrency = editingTransaction.Currency;
                        amount = editingTransaction.Amount;
                        description = editingTransaction.Description;
                        timezoneId = string.IsNullOrEmpty(editingTransaction.Timezone) ? TimeZoneInfo.Local.Id : editingTransaction.Timezone;
                        try
                        {
                            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                            transactionDate = TimeZoneInfo.ConvertTimeFromUtc(editingTransaction.Date.UtcDateTime, tz);
                        }
                        catch
                        {
                            transactionDate = editingTransaction.Date.LocalDateTime;
                        }
                        selectedMemberSlug = editingTransaction.Split.Keys.FirstOrDefault() ?? "";
                        isAmountDirty = true;
                    }
                }

                if (editingTransaction == null)
                {
                    amount = 0;
                    timezoneId = TimeZoneInfo.Local.Id;
                    if (!string.IsNullOrEmpty(currency) && config.Currencies.ContainsKey(currency))
                    {
                        selectedCurrency = currency;
                    }
                    else if (config.Currencies.Any())
                    {
                        selectedCurrency = config.Currencies.Keys.First();
                    }

                    if (!string.IsNullOrEmpty(member) && config.Members.ContainsKey(member))
                    {
                        selectedMemberSlug = member;
                    }

                    SetDefaultAmount();
                }
            }
        }

        private void SetDefaultAmount()
        {
            if (isAmountDirty || config == null || allTransactions == null || string.IsNullOrEmpty(selectedCurrency) || string.IsNullOrEmpty(selectedMemberSlug))
            {
                return;
            }

            if (config.Currencies.TryGetValue(selectedCurrency, out var currencyConfig))
            {
                var target = currencyConfig.ExpectedQuotaPerMember;
                var contributed = allTransactions
                    .Where(t => t.Type == "contribution" && t.Currency == selectedCurrency && t.Split.ContainsKey(selectedMemberSlug))
                    .Sum(t => t.Amount);

                amount = Math.Max(0, target - contributed);
            }
        }

        private bool HasChanges()
        {
            if (editingTransaction == null) return true; // Always enabled for new
            if (originalTxJson == null) return true;

            var currentTx = BuildTransaction();
            currentTx.Description = currentTx.Description?.Trim() ?? "";

            var original = System.Text.Json.JsonSerializer.Deserialize<Transaction>(originalTxJson);
            if (original == null) return true;
            original.Description = original.Description?.Trim() ?? "";

            if (currentTx.Amount != original.Amount) return true;
            if (currentTx.Description != original.Description) return true;
            if (currentTx.Currency != original.Currency) return true;
            if (currentTx.Timezone != original.Timezone) return true;
            if (currentTx.Date.ToUnixTimeSeconds() != original.Date.ToUnixTimeSeconds()) return true;

            // Compare Split (for contribution it's just one member)
            if (currentTx.Split.Count != original.Split.Count) return true;
            var currentMember = currentTx.Split.Keys.FirstOrDefault();
            var originalMember = original.Split.Keys.FirstOrDefault();
            if (currentMember != originalMember) return true;

            return false;
        }

        private Transaction BuildTransaction()
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); } catch { tz = TimeZoneInfo.Local; }
            var offset = tz.GetUtcOffset(transactionDate);
            var finalDate = new DateTimeOffset(DateTime.SpecifyKind(transactionDate, DateTimeKind.Unspecified), offset);

            return new Transaction
            {
                Id = editingTransaction?.Id ?? "",
                Type = "contribution",
                Date = finalDate,
                Timezone = timezoneId,
                Currency = selectedCurrency,
                Amount = amount,
                Description = description,
                Author = authorName,
                Split = new Dictionary<string, SplitInfo> { { selectedMemberSlug, new SplitInfo { Amount = amount, Manual = true } } }
            };
        }

        private void ToggleMemberSelector()
        {
            isMemberSelectorOpen = !isMemberSelectorOpen;
            if (isMemberSelectorOpen)
            {
                shouldScroll = true;
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (shouldScroll && isMemberSelectorOpen)
            {
                shouldScroll = false;
                await JSRuntime.InvokeVoidAsync("headerLogic.scrollIntoView", "#selected-member-item", "center");
            }
        }

        private void SelectMember(string slug)
        {
            selectedMemberSlug = slug;
            isMemberSelectorOpen = false;
            if (editingTransaction == null) SetDefaultAmount();
        }

        private decimal GetMemberTotal(string slug)
        {
            if (allTransactions == null) return 0;
            return allTransactions
                .Where(t => t.Type == "contribution" && t.Currency == selectedCurrency && t.Split != null && t.Split.ContainsKey(slug))
                .Sum(t => t.Amount);
        }

        private void SelectCurrency(string currencyCode)
        {
            if (editingTransaction == null)
            {
                selectedCurrency = currencyCode;
                SetDefaultAmount();
            }
        }

        private void ToggleMenu() => isMenuOpen = !isMenuOpen;

        private async Task DeleteTransaction()
        {
            isMenuOpen = false;
            if (editingTransaction == null) return;

            bool confirm = await AlertService.ConfirmAsync(
                "Elimina Versamento",
                "Sei sicuro di voler eliminare questo versamento? L'operazione non può essere annullata.",
                "Elimina",
                "Annulla");

            if (!confirm) return;

            try
            {
                await Storage.SaveTransactionAsync(tripSlug, editingTransaction, deviceId, isDelete: true);
                await GoBack();
            }
            catch (Exception ex)
            {
                errorMessage = "Errore durante l'eliminazione: " + ex.Message;
            }
        }

        private async Task GoBack()
        {
            if (!string.IsNullOrEmpty(member))
            {
                Nav.NavigateTo($"/trip/{tripSlug}/member/{member}?currency={selectedCurrency}");
            }
            else
            {
                Nav.NavigateTo($"/trip/{tripSlug}?currency={selectedCurrency}");
            }
            await Task.CompletedTask;
        }

        private void OnAmountChanged(ChangeEventArgs e)
        {
            isAmountDirty = true;
            var input = e.Value?.ToString()?.Replace(".", ",");
            if (decimal.TryParse(input, out decimal val))
            {
                amount = Math.Round(val, GetDecimals());
            }
        }

        private void TrimDescription()
        {
            description = description?.Trim() ?? "";
        }

        private string GetCurrencySymbol()
        {
            if (config != null && config.Currencies.TryGetValue(selectedCurrency, out var c))
            {
                return c.Symbol;
            }
            return "";
        }

        private int GetDecimals()
        {
            if (config != null && config.Currencies.TryGetValue(selectedCurrency, out var c))
            {
                return c.Decimals;
            }
            return 2;
        }

        private string GetStep()
        {
            int decimals = GetDecimals();
            if (decimals <= 0) return "1";
            return (1 / Math.Pow(10, decimals)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private async Task HandleSubmit()
        {
            errorMessage = "";

            if (config == null) return;

            description = description?.Trim() ?? "";

            if (amount <= 0)
            {
                errorMessage = "Inserire un importo valido.";
                return;
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                errorMessage = "Inserire una descrizione.";
                return;
            }

            if (string.IsNullOrEmpty(selectedMemberSlug))
            {
                errorMessage = "Selezionare un partecipante.";
                return;
            }

            isSubmitting = true;

            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            }
            catch
            {
                tz = TimeZoneInfo.Local;
            }
            
            var offset = tz.GetUtcOffset(transactionDate);
            var finalDate = new DateTimeOffset(DateTime.SpecifyKind(transactionDate, DateTimeKind.Unspecified), offset);

            var transaction = new Transaction
            {
                Id = editingTransaction?.Id ?? TransactionIdGenerator.GenerateId(),
                Type = "contribution",
                Date = finalDate,
                Timezone = timezoneId,
                Currency = selectedCurrency,
                Amount = amount,
                Description = description,
                Author = authorName,
                Split = new Dictionary<string, SplitInfo> { { selectedMemberSlug, new SplitInfo { Amount = amount, Manual = true } } }
            };

            try
            {
                await Storage.SaveTransactionAsync(tripSlug, transaction, deviceId);
                
                if (config.Members.TryGetValue(selectedMemberSlug, out var member))
                {
                    bool confirm = await AlertService.ConfirmAsync("Invia ricevuta", $"Vuoi inviare una ricevuta via email a {member.Name}?", "Sì", "No");
                    if (confirm)
                    {
                        try
                        {
                            var updatedTransactions = await Storage.GetTransactionsAsync(tripSlug);
                            var body = ReceiptGenerator.GenerateContributionText(config, selectedMemberSlug, transaction, updatedTransactions);
                            var recipients = string.IsNullOrEmpty(member.Email) ? Array.Empty<string>() : new[] { member.Email };
                            await EmailService.SendEmailAsync($"{config.Name} - Riepilogo versamenti", body, recipients);
                        }
                        catch (Exception emailEx)
                        {
                            await AlertService.ShowAlertAsync("Errore Email", "Non è stato possibile preparare l'email: " + emailEx.Message);
                        }
                    }
                }

                await GoBack();
            }
            catch (Exception ex)
            {
                errorMessage = "Errore durante il salvataggio: " + ex.Message;
                isSubmitting = false;
            }
        }
    }
}

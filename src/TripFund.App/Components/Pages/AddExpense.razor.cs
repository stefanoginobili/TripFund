using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class AddExpense
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private IAlertService Alerts { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";
        [SupplyParameterFromQuery] public string? currency { get; set; }
        [SupplyParameterFromQuery] public string? edit { get; set; }

        private TripConfig? config;
        private LocalTripStorageService.TransactionVersionInfo? editingInfo;
        private string selectedCurrency = "";
        private decimal totalAmount;
        private string description = "";
        private DateTime transactionDate = DateTime.Now;
        private string timezoneId = TimeZoneInfo.Local.Id;
        private List<MemberSplitInfo> memberSplits = new();
        private List<AttachmentInfo> attachments = new();
        private LocationInfo? locationInfo;
        private string errorMessage = "";
        private bool isSubmitting = false;
        private bool isLocating = false;
        private bool isMenuOpen = false;
        private string deviceId = "";
        private string authorName = "";

        protected override async Task OnInitializedAsync()
        {
            config = await Storage.GetTripConfigAsync(tripSlug);
            var settings = await Storage.GetAppSettingsAsync();
            deviceId = settings?.DeviceId ?? "unknown";
            authorName = settings?.AuthorName ?? "Unknown";

            if (config != null)
            {
                memberSplits = config.Members.Select(m => new MemberSplitInfo
                {
                    Slug = m.Key,
                    Name = m.Value.Name,
                    Avatar = m.Value.Avatar,
                    IsIncluded = false // Default to false if editing
                }).ToList();

                if (!string.IsNullOrEmpty(edit))
                {
                    editingInfo = await Storage.GetLatestTransactionVersionWithMetadataAsync(tripSlug, edit);
                    if (editingInfo != null && editingInfo.Transaction != null && editingInfo.Transaction.Type == "expense")
                    {
                        var tx = editingInfo.Transaction;
                        selectedCurrency = tx.Currency;
                        totalAmount = tx.Amount;
                        description = tx.Description;
                        timezoneId = string.IsNullOrEmpty(tx.Timezone) ? TimeZoneInfo.Local.Id : tx.Timezone;
                        try
                        {
                            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                            transactionDate = TimeZoneInfo.ConvertTimeFromUtc(tx.Date.UtcDateTime, tz);
                        }
                        catch
                        {
                            transactionDate = tx.Date.LocalDateTime;
                        }
                        locationInfo = tx.Location;
                        
                        foreach (var m in memberSplits)
                        {
                            if (tx.Split.TryGetValue(m.Slug, out SplitInfo? val))
                            {
                                m.IsIncluded = true;
                                m.ManualAmount = val.Amount;
                                m.CalculatedAmount = val.Amount; 
                                m.IsAuto = !val.Manual;
                            }
                        }

                        foreach (var att in tx.Attachments)
                        {
                            attachments.Add(new AttachmentInfo { FileName = att, IsExisting = true });
                        }
                    }
                }

                if (editingInfo == null)
                {
                    totalAmount = 0;
                    timezoneId = TimeZoneInfo.Local.Id;
                    if (!string.IsNullOrEmpty(currency) && config.Currencies.ContainsKey(currency))
                    {
                        selectedCurrency = currency;
                    }
                    else if (config.Currencies.Any())
                    {
                        selectedCurrency = config.Currencies.Keys.First();
                    }
                    
                    // Default all to included/auto if new
                    foreach (var m in memberSplits) m.IsIncluded = true;
                    RecalculateSplit();
                }
            }
        }

        private void SelectCurrency(string currencyCode)
        {
            if (editingInfo == null) selectedCurrency = currencyCode;
        }

        private void ToggleMenu() => isMenuOpen = !isMenuOpen;

        private async Task DeleteTransaction()
        {
            isMenuOpen = false;
            if (editingInfo?.Transaction == null) return;

            bool confirm = await Alerts.ConfirmAsync(
                "Elimina Spesa",
                "Sei sicuro di voler eliminare questa spesa? L'operazione non può essere annullata.",
                "Elimina",
                "Annulla");

            if (!confirm) return;

            try
            {
                await Storage.SaveTransactionAsync(tripSlug, editingInfo.Transaction, deviceId, isDelete: true);
                await GoBack();
            }
            catch (Exception ex)
            {
                errorMessage = "Errore durante l'eliminazione: " + ex.Message;
            }
        }

        private async Task GoBack()
        {
            Nav.NavigateTo($"/trip/{tripSlug}?currency={selectedCurrency}");
            await Task.CompletedTask;
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

        private void OnAmountChanged(ChangeEventArgs e)
        {
            var input = e.Value?.ToString()?.Replace(".", ",");
            if (decimal.TryParse(input, out decimal val))
            {
                totalAmount = Math.Round(val, GetDecimals());
                RecalculateSplit();
            }
        }

        private void ToggleMember(MemberSplitInfo member)
        {
            member.IsIncluded = !member.IsIncluded;
            RecalculateSplit();
        }

        private void ToggleAuto(MemberSplitInfo member)
        {
            member.IsAuto = !member.IsAuto;
            if (!member.IsAuto) member.ManualAmount = member.CalculatedAmount;
            RecalculateSplit();
        }

        private void OnManualAmountChanged(MemberSplitInfo member, ChangeEventArgs e)
        {
            var input = e.Value?.ToString()?.Replace(".", ",");
            if (decimal.TryParse(input, out decimal val))
            {
                member.ManualAmount = val;
                RecalculateSplit();
            }
        }

        private void RecalculateSplit()
        {
            var included = memberSplits.Where(m => m.IsIncluded).ToList();
            if (!included.Any()) return;

            var manual = included.Where(m => !m.IsAuto).ToList();
            var autos = included.Where(m => m.IsAuto).ToList();

            decimal manualSum = manual.Sum(m => m.ManualAmount);
            decimal toDistribute = totalAmount - manualSum;

            if (autos.Any())
            {
                int decimals = GetDecimals();
                decimal autoShare = Math.Round(toDistribute / autos.Count, decimals, MidpointRounding.ToZero);
                foreach (var m in autos) m.CalculatedAmount = autoShare;

                // Remainder handling
                decimal currentTotal = manualSum + (autoShare * autos.Count);
                decimal remainder = totalAmount - currentTotal;
                
                if (remainder != 0)
                {
                    // The remainder is at most (0.01 * (autos.Count - 1)) if we rounded down
                    // We add/subtract 1 unit of the smallest decimal to participants until remainder is 0
                    decimal unit = (decimal)Math.Pow(10, -decimals);
                    int count = (int)Math.Abs(remainder / unit);
                    decimal sign = Math.Sign(remainder);
                    
                    for (int i = 0; i < count && i < autos.Count; i++)
                    {
                        autos[i].CalculatedAmount += sign * unit;
                    }
                }
            }
        }

        private async Task CapturePhoto()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                }

                if (status != PermissionStatus.Granted)
                {
                    errorMessage = "Permesso fotocamera non concesso.";
                    return;
                }

                var photo = await MediaPicker.Default.CapturePhotoAsync();
                if (photo != null)
                {
                    attachments.Add(new AttachmentInfo { FileName = photo.FileName, Stream = await photo.OpenReadAsync(), Extension = Path.GetExtension(photo.FileName) });
                }
            }
            catch (Exception ex)
            {
                errorMessage = "Errore durante lo scatto della foto: " + ex.Message;
            }
        }

        private async Task PickFile()
        {
            try
            {
                var result = await FilePicker.Default.PickAsync();
                if (result != null)
                {
                    attachments.Add(new AttachmentInfo { FileName = result.FileName, Stream = await result.OpenReadAsync(), Extension = Path.GetExtension(result.FileName) });
                }
            }
            catch (Exception ex)
            {
                errorMessage = "Errore durante la selezione del file: " + ex.Message;
            }
        }

        private void RemoveAttachment(AttachmentInfo att)
        {
            attachments.Remove(att);
        }

        private string GetAttachmentDisplayName(AttachmentInfo att)
        {
            if (att.IsExisting) return att.FileName;

            // For new attachments, we preview what the name WILL be
            var existing = attachments.Where(a => a.IsExisting).Select(a => a.FileName).ToList();
            int maxNum = 0;
            var regex = new System.Text.RegularExpressions.Regex(@"^Attachment-(?<num>\d+)\..*$");
            foreach (var name in existing)
            {
                var m = regex.Match(name);
                if (m.Success && int.TryParse(m.Groups["num"].Value, out int n) && n > maxNum) maxNum = n;
            }

            // Find position of this item among new attachments
            var newOnes = attachments.Where(a => !a.IsExisting).ToList();
            int index = newOnes.IndexOf(att);
            if (index < 0) return att.FileName;

            return $"Attachment-{(maxNum + index + 1):D2}{att.Extension}";
        }

        private async Task FetchLocation()
        {
            try
            {
                isLocating = true;
                errorMessage = "";
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (status != PermissionStatus.Granted)
                {
                    errorMessage = "Permesso posizione non concesso.";
                    return;
                }

                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
                if (location != null)
                {
                    string name = "Posizione acquisita";
                    
                    if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                    {
                        try
                        {
                            var placemarks = await Geocoding.Default.GetPlacemarksAsync(location.Latitude, location.Longitude);
                            var placemark = placemarks?.FirstOrDefault();
                            if (placemark != null)
                            {
                                name = $"{placemark.Thoroughfare} {placemark.SubThoroughfare}, {placemark.Locality}".Trim(' ', ',');
                                if (string.IsNullOrWhiteSpace(name)) name = placemark.Locality ?? placemark.CountryName ?? "Posizione acquisita";
                            }
                        }
                        catch { /* Ignore geocoding errors */ }
                    }

                    locationInfo = new LocationInfo
                    {
                        Latitude = location.Latitude,
                        Longitude = location.Longitude,
                        Name = name 
                    };
                }
            }
            catch (Exception ex)
            {
                errorMessage = "Impossibile acquisire la posizione: " + ex.Message;
            }
            finally
            {
                isLocating = false;
            }
        }

        private async Task HandleSubmit()
        {
            errorMessage = "";
            
            if (totalAmount <= 0) { errorMessage = "Importo non valido."; return; }
            if (string.IsNullOrWhiteSpace(description)) { errorMessage = "Inserire una descrizione."; return; }
            
            var included = memberSplits.Where(m => m.IsIncluded).ToList();
            if (!included.Any()) { errorMessage = "Selezionare almeno un partecipante."; return; }

            isSubmitting = true;

            var splitDict = new Dictionary<string, SplitInfo>();
            foreach (var m in included)
            {
                splitDict[m.Slug] = new SplitInfo 
                { 
                    Amount = m.IsAuto ? m.CalculatedAmount : m.ManualAmount, 
                    Manual = !m.IsAuto 
                };
            }

            // Verify split sum
            if (splitDict.Values.Sum(s => s.Amount) != totalAmount)
            {
                errorMessage = "La somma delle quote non corrisponde al totale.";
                isSubmitting = false;
                return;
            }

            var existingAttachments = attachments.Where(a => a.IsExisting).Select(a => a.FileName).ToList();
            
            // Find max NN from existing attachments
            int maxNum = 0;
            var attachmentRegex = new System.Text.RegularExpressions.Regex(@"^Attachment-(?<num>\d+)\..*$");
            foreach (var att in existingAttachments)
            {
                var match = attachmentRegex.Match(att);
                if (match.Success && int.TryParse(match.Groups["num"].Value, out int n))
                {
                    if (n > maxNum) maxNum = n;
                }
            }

            var newAttachmentsToProcess = attachments.Where(a => !a.IsExisting).ToList();
            var processedNewAttachments = new List<(AttachmentInfo info, string newName)>();
            
            foreach (var info in newAttachmentsToProcess)
            {
                maxNum++;
                processedNewAttachments.Add((info, $"Attachment-{maxNum:D2}{info.Extension}"));
            }

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
                Id = editingInfo?.Transaction?.Id ?? TransactionIdGenerator.GenerateId(),
                Type = "expense",
                Date = finalDate,
                Timezone = timezoneId,
                Currency = selectedCurrency,
                Amount = totalAmount,
                Description = description,
                Author = authorName,
                Split = splitDict,
                Location = locationInfo,
                Attachments = existingAttachments.Concat(processedNewAttachments.Select(na => na.newName)).ToList()
            };

            var attachmentsDict = new Dictionary<string, byte[]>();
            foreach (var na in processedNewAttachments)
            {
                using var ms = new MemoryStream();
                await na.info.Stream.CopyToAsync(ms);
                attachmentsDict[na.newName] = ms.ToArray();
            }

            try
            {
                await Storage.SaveTransactionAsync(tripSlug, transaction, deviceId, attachments: attachmentsDict);
                await GoBack();
            }
            catch (Exception ex)
            {
                errorMessage = "Errore durante il salvataggio: " + ex.Message;
                isSubmitting = false;
            }
        }

        private class MemberSplitInfo
        {
            public string Slug { get; set; } = "";
            public string Name { get; set; } = "";
            public string Avatar { get; set; } = "";
            public bool IsIncluded { get; set; } = true;
            public bool IsAuto { get; set; } = true;
            public decimal ManualAmount { get; set; }
            public decimal CalculatedAmount { get; set; }
        }

        private class AttachmentInfo
        {
            public string FileName { get; set; } = "";
            public string Extension { get; set; } = "";
            public Stream Stream { get; set; } = Stream.Null;
            public bool IsExisting { get; set; }
        }
    }
}

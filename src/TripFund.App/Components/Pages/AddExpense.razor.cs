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
        [Inject] private IThumbnailService Thumbnails { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";
        [SupplyParameterFromQuery] public string? currency { get; set; }
        [SupplyParameterFromQuery] public string? edit { get; set; }

        private TripConfig? config;
        private LocalTripStorageService.TransactionVersionInfo? editingInfo;
        private string? originalTxJson;
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
                        originalTxJson = System.Text.Json.JsonSerializer.Serialize(tx);
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
                        locationInfo = tx.Location != null ? new LocationInfo 
                        { 
                            Latitude = tx.Location.Latitude, 
                            Longitude = tx.Location.Longitude, 
                            Name = tx.Location.Name 
                        } : null;
                        
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
                            var info = new AttachmentInfo 
                            { 
                                FileName = att.Name, 
                                OriginalName = att.OriginalName, 
                                CreatedAt = att.CreatedAt, 
                                IsExisting = true 
                            };
                            
                            var tz = TimeZoneInfo.Local;
                            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); } catch { }
                            var localizedTime = TimeZoneInfo.ConvertTimeFromUtc(att.CreatedAt, tz);
                            info.DisplayTimestamp = localizedTime.ToString("dd/MM/yyyy HH:mm");

                            var path = await Storage.GetAttachmentPath(tripSlug, edit, att.Name);
                            if (!string.IsNullOrEmpty(path))
                            {
                                var ext = Path.GetExtension(att.Name).ToLower();
                                var nativeThumb = await Thumbnails.GetThumbnailBase64Async(path);
                                if (!string.IsNullOrEmpty(nativeThumb))
                                {
                                    info.PreviewUrl = nativeThumb;
                                    info.IsImage = true;
                                }
                                else if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }.Contains(ext))
                                {
                                    try
                                    {
                                        var bytes = await File.ReadAllBytesAsync(path);
                                        info.PreviewUrl = $"data:image/{ext.TrimStart('.')};base64,{Convert.ToBase64String(bytes)}";
                                        info.IsImage = true;
                                    }
                                    catch { }
                                }
                            }

                            attachments.Add(info);
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

        private bool HasChanges()
        {
            if (editingInfo == null) return true; // Always enabled for new
            if (originalTxJson == null) return true;

            var currentTx = BuildTransaction();
            
            // Normalize for comparison
            currentTx.Description = currentTx.Description?.Trim() ?? "";
            if (currentTx.Location != null) currentTx.Location.Name = currentTx.Location.Name?.Trim() ?? "";

            var original = System.Text.Json.JsonSerializer.Deserialize<Transaction>(originalTxJson);
            if (original == null) return true;
            original.Description = original.Description?.Trim() ?? "";
            if (original.Location != null) original.Location.Name = original.Location.Name?.Trim() ?? "";

            // Custom comparison because DateTimeOffset might have slight differences or different formatting
            // and we want to compare the fields carefully.
            
            if (currentTx.Amount != original.Amount) return true;
            if (currentTx.Description != original.Description) return true;
            if (currentTx.Currency != original.Currency) return true;
            if (currentTx.Timezone != original.Timezone) return true;
            
            // Compare dates with a small tolerance or by formatted string to avoid precision issues
            if (currentTx.Date.ToUnixTimeSeconds() != original.Date.ToUnixTimeSeconds()) return true;

            // Compare Split
            if (currentTx.Split.Count != original.Split.Count) return true;
            foreach (var kvp in currentTx.Split)
            {
                if (!original.Split.TryGetValue(kvp.Key, out var originalSplit)) return true;
                if (kvp.Value.Amount != originalSplit.Amount) return true;
                if (kvp.Value.Manual != originalSplit.Manual) return true;
            }

            // Compare Location
            if (currentTx.Location == null && original.Location != null) return true;
            if (currentTx.Location != null && original.Location == null) return true;
            if (currentTx.Location != null && original.Location != null)
            {
                if (currentTx.Location.Latitude != original.Location.Latitude) return true;
                if (currentTx.Location.Longitude != original.Location.Longitude) return true;
                if (currentTx.Location.Name != original.Location.Name) return true;
            }

            // Compare Attachments (names)
            if (currentTx.Attachments.Count != original.Attachments.Count) return true;
            var currentAttNames = currentTx.Attachments.Select(a => a.Name).OrderBy(n => n);
            var originalAttNames = original.Attachments.Select(a => a.Name).OrderBy(n => n);
            if (!currentAttNames.SequenceEqual(originalAttNames)) return true;

            return false;
        }

        private Transaction BuildTransaction()
        {
            var included = memberSplits.Where(m => m.IsIncluded).ToList();
            var splitDict = new Dictionary<string, SplitInfo>();
            foreach (var m in included)
            {
                splitDict[m.Slug] = new SplitInfo 
                { 
                    Amount = m.IsAuto ? m.CalculatedAmount : m.ManualAmount, 
                    Manual = !m.IsAuto 
                };
            }

            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); } catch { tz = TimeZoneInfo.Local; }
            var offset = tz.GetUtcOffset(transactionDate);
            var finalDate = new DateTimeOffset(DateTime.SpecifyKind(transactionDate, DateTimeKind.Unspecified), offset);

            var txAttachments = new List<TransactionAttachment>();
            foreach (var att in attachments)
            {
                txAttachments.Add(new TransactionAttachment
                {
                    Name = att.IsExisting ? att.FileName : GetAttachmentDisplayName(att),
                    OriginalName = att.OriginalName,
                    CreatedAt = att.CreatedAt
                });
            }

            return new Transaction
            {
                Id = editingInfo?.Transaction?.Id ?? "",
                Type = "expense",
                Date = finalDate,
                Timezone = timezoneId,
                Currency = selectedCurrency,
                Amount = totalAmount,
                Description = description,
                Author = authorName,
                Split = splitDict,
                Location = locationInfo != null ? new LocationInfo { Latitude = locationInfo.Latitude, Longitude = locationInfo.Longitude, Name = locationInfo.Name } : null,
                Attachments = txAttachments
            };
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

        private void SelectCurrency(string currencyCode)
        {
            if (editingInfo == null) selectedCurrency = currencyCode;
        }

        private void OnTimezoneChanged(string newTzId)
        {
            timezoneId = newTzId;
            UpdateAttachmentTimestamps();
        }

        private void UpdateAttachmentTimestamps()
        {
            var tz = TimeZoneInfo.Local;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); } catch { }

            foreach (var info in attachments)
            {
                var localizedTime = TimeZoneInfo.ConvertTimeFromUtc(info.CreatedAt, tz);
                info.DisplayTimestamp = localizedTime.ToString("dd/MM/yyyy HH:mm");
            }
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

        private void TrimDescription()
        {
            description = description?.Trim() ?? "";
        }

        private void TrimLocation()
        {
            if (locationInfo != null)
            {
                locationInfo.Name = locationInfo.Name?.Trim() ?? "";
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
                    var info = new AttachmentInfo 
                    { 
                        FileName = photo.FileName, 
                        OriginalName = photo.FileName, 
                        CreatedAt = DateTime.UtcNow, 
                        Stream = await photo.OpenReadAsync(), 
                        Extension = Path.GetExtension(photo.FileName) 
                    };
                    await GeneratePreviewAsync(info);
                    attachments.Add(info);
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
                    var info = new AttachmentInfo 
                    { 
                        FileName = result.FileName, 
                        OriginalName = result.FileName, 
                        CreatedAt = DateTime.UtcNow, 
                        Stream = await result.OpenReadAsync(), 
                        Extension = Path.GetExtension(result.FileName) 
                    };
                    await GeneratePreviewAsync(info);
                    attachments.Add(info);
                }
            }
            catch (Exception ex)
            {
                errorMessage = "Errore durante la selezione del file: " + ex.Message;
            }
        }

        private async Task GeneratePreviewAsync(AttachmentInfo info)
        {
            var tz = TimeZoneInfo.Local;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); } catch { }
            var localizedTime = TimeZoneInfo.ConvertTimeFromUtc(info.CreatedAt, tz);
            info.DisplayTimestamp = localizedTime.ToString("dd/MM/yyyy HH:mm");

            if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }.Contains(info.Extension.ToLower()))
            {
                try
                {
                    using var ms = new MemoryStream();
                    await info.Stream.CopyToAsync(ms);
                    info.Stream.Position = 0; // Reset for later use
                    info.PreviewUrl = $"data:image/{info.Extension.TrimStart('.').ToLower()};base64,{Convert.ToBase64String(ms.ToArray())}";
                    info.IsImage = true;
                }
                catch { }
            }
        }

        private void RemoveAttachment(AttachmentInfo att)
        {
            attachments.Remove(att);
        }

        private string GetAttachmentDisplayName(AttachmentInfo att)
        {
            if (att.IsExisting) return att.FileName;
            var timestamp = att.CreatedAt.ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ");
            return $"ATT_{timestamp}";
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
            
            description = description?.Trim() ?? "";
            if (locationInfo != null) locationInfo.Name = locationInfo.Name?.Trim() ?? "";

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

            var newAttachmentsToProcess = attachments.Where(a => !a.IsExisting).ToList();
            var processedNewAttachments = new List<(AttachmentInfo info, string newName)>();
            
            foreach (var info in newAttachmentsToProcess)
            {
                var newName = GetAttachmentDisplayName(info);
                if (processedNewAttachments.Any(p => p.newName == newName))
                {
                    errorMessage = "Due allegati non possono avere lo stesso timestamp (millisecondo).";
                    isSubmitting = false;
                    return;
                }
                processedNewAttachments.Add((info, newName));
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

            var txAttachments = new List<TransactionAttachment>();
            foreach (var att in attachments.Where(a => a.IsExisting))
            {
                txAttachments.Add(new TransactionAttachment
                {
                    Name = att.FileName,
                    OriginalName = att.OriginalName,
                    CreatedAt = att.CreatedAt
                });
            }
            foreach (var na in processedNewAttachments)
            {
                txAttachments.Add(new TransactionAttachment
                {
                    Name = na.newName,
                    OriginalName = na.info.OriginalName,
                    CreatedAt = na.info.CreatedAt
                });
            }

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
                Attachments = txAttachments
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
            public string OriginalName { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public string DisplayTimestamp { get; set; } = "";
            public string? PreviewUrl { get; set; }
            public bool IsImage { get; set; }
            public string Extension { get; set; } = "";
            public Stream Stream { get; set; } = Stream.Null;
            public bool IsExisting { get; set; }
        }
    }
}

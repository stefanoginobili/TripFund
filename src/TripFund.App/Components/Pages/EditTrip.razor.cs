using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class EditTrip
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IAlertService Alerts { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";

        private TripConfig? config;
        private string? originalConfigJson;
        private string error = "";
        private string deviceId = "";
        private string authorName = "";

        private string newMemberName = "";
        private string newMemberSlug = "";
        private string newMemberEmail = "";
        private string newMemberAvatar = "👤";
        private bool showEmojiPicker = false;
        private bool isAddingMember = false;
        private string? openMenuMemberSlug = null;
        private string? editingMemberSlug = null;
        private string[] emojis = new[] { 
            "👤", "👨", "👩", "🧔", "👴", "👵", "👶", "🦁", "🐯", "🐼", "🐨", "🦊", 
            "🍕", "🍔", "🍦", "🍩", "🍷", "🍺", "☕", "⚽", "🏀", "🎮", "🚗", "✈️", 
            "🏖️", "🏔️", "🗼", "🗽", "🌈", "🔥", "✨", "❤️", "🎒", "📸", "🗺️", "⛺" 
        };

        private bool isHeaderMenuOpen = false;
        private bool isReadonly = false;

        private void ToggleHeaderMenu() => isHeaderMenuOpen = !isHeaderMenuOpen;

        protected override async Task OnInitializedAsync()
        {
            config = await Storage.GetTripConfigAsync(tripSlug);
            if (config != null)
            {
                originalConfigJson = System.Text.Json.JsonSerializer.Serialize(config);
            }
            
            var registry = await Storage.GetTripRegistryAsync();
            if (registry != null && registry.Trips.TryGetValue(tripSlug, out var entry))
            {
                isReadonly = entry.RemoteStorage?.Readonly ?? false;
            }

            var settings = await Storage.GetAppSettingsAsync();
            deviceId = settings?.DeviceId ?? "";
            authorName = settings?.AuthorName ?? "";
        }

        private bool HasChanges()
        {
            if (config == null || originalConfigJson == null) return false;
            
            // Trim values before comparison to ensure manual restoration to original trimmed value is detected
            var currentConfig = new TripConfig
            {
                Id = config.Id,
                Name = config.Name?.Trim() ?? "",
                Description = config.Description?.Trim() ?? "",
                StartDate = config.StartDate,
                EndDate = config.EndDate,
                Currencies = config.Currencies.ToDictionary(k => k.Key, v => new Currency 
                { 
                    Symbol = v.Value.Symbol?.Trim() ?? "", 
                    Name = v.Value.Name?.Trim() ?? "", 
                    Decimals = v.Value.Decimals, 
                    ExpectedQuotaPerMember = v.Value.ExpectedQuotaPerMember 
                }),
                Members = config.Members.ToDictionary(k => k.Key, v => new User 
                { 
                    Name = v.Value.Name?.Trim() ?? "", 
                    Email = v.Value.Email?.Trim() ?? "", 
                    Avatar = v.Value.Avatar 
                })
            };

            var currentJson = System.Text.Json.JsonSerializer.Serialize(currentConfig);
            
            // We also need to compare against a "normalized" original
            var original = System.Text.Json.JsonSerializer.Deserialize<TripConfig>(originalConfigJson);
            if (original == null) return false;
            
            var normalizedOriginal = new TripConfig
            {
                Id = original.Id,
                Name = original.Name?.Trim() ?? "",
                Description = original.Description?.Trim() ?? "",
                StartDate = original.StartDate,
                EndDate = original.EndDate,
                Currencies = original.Currencies.ToDictionary(k => k.Key, v => new Currency 
                { 
                    Symbol = v.Value.Symbol?.Trim() ?? "", 
                    Name = v.Value.Name?.Trim() ?? "", 
                    Decimals = v.Value.Decimals, 
                    ExpectedQuotaPerMember = v.Value.ExpectedQuotaPerMember 
                }),
                Members = original.Members.ToDictionary(k => k.Key, v => new User 
                { 
                    Name = v.Value.Name?.Trim() ?? "", 
                    Email = v.Value.Email?.Trim() ?? "", 
                    Avatar = v.Value.Avatar 
                })
            };
            var normalizedOriginalJson = System.Text.Json.JsonSerializer.Serialize(normalizedOriginal);

            return currentJson != normalizedOriginalJson;
        }

        private void ToggleEmojiPicker() => showEmojiPicker = !showEmojiPicker;
        
        private void SelectEmoji(string emoji)
        {
            newMemberAvatar = emoji;
            showEmojiPicker = false;
        }

        private void OnNewMemberNameInput(ChangeEventArgs e)
        {
            newMemberName = e.Value?.ToString() ?? "";
            newMemberSlug = SlugUtility.GenerateSlug(newMemberName);
        }

        private void TrimMemberName()
        {
            newMemberName = newMemberName?.Trim() ?? "";
        }

        private void TrimMemberEmail()
        {
            newMemberEmail = newMemberEmail?.Trim() ?? "";
        }

        private void AddMember()
        {
            if (config == null) return;
            var trimmedName = newMemberName.Trim();
            var trimmedEmail = newMemberEmail.Trim();
            
            if (string.IsNullOrWhiteSpace(trimmedName)) { error = "Il nome è obbligatorio."; return; }
            if (string.IsNullOrWhiteSpace(newMemberSlug)) { error = "Lo slug è obbligatorio."; return; }
            
            // If not editing, check for duplicates
            if (editingMemberSlug == null && config.Members.ContainsKey(newMemberSlug))
            {
                error = "Partecipante già presente.";
                return;
            }

            config.Members[newMemberSlug] = new User
            {
                Name = trimmedName,
                Email = string.IsNullOrWhiteSpace(trimmedEmail) ? null : trimmedEmail,
                Avatar = string.IsNullOrWhiteSpace(newMemberAvatar) ? "👤" : newMemberAvatar
            };

            newMemberName = "";
            newMemberSlug = "";
            newMemberEmail = "";
            newMemberAvatar = "👤";
            error = "";
            isAddingMember = false;
            editingMemberSlug = null;
        }

        private void RemoveMember(string slug)
        {
            config?.Members.Remove(slug);
        }

        private void ToggleMemberMenu(string slug)
        {
            if (openMenuMemberSlug == slug) openMenuMemberSlug = null;
            else openMenuMemberSlug = slug;
        }

        private async Task StartEditMember(string slug, User m)
        {
            isAddingMember = false;
            editingMemberSlug = slug;
            newMemberSlug = slug;
            newMemberName = m.Name;
            newMemberEmail = m.Email ?? "";
            newMemberAvatar = m.Avatar;
            openMenuMemberSlug = null;
            await Task.Yield();
            StateHasChanged();
        }

        private void CancelMemberEdit()
        {
            editingMemberSlug = null;
            newMemberSlug = "";
            newMemberName = "";
            newMemberEmail = "";
            newMemberAvatar = "👤";
        }

        private void MoveMemberUp(string slug)
        {
            if (config == null) return;
            var keys = config.Members.Keys.ToList();
            int index = keys.IndexOf(slug);
            if (index <= 0) return;

            var prevKey = keys[index - 1];
            var currentVal = config.Members[slug];
            var prevVal = config.Members[prevKey];

            var newDict = new Dictionary<string, User>();
            foreach (var key in keys)
            {
                if (key == prevKey) newDict[slug] = currentVal;
                else if (key == slug) newDict[prevKey] = prevVal;
                else newDict[key] = config.Members[key];
            }
            config.Members = newDict;
            openMenuMemberSlug = null;
        }

        private void MoveMemberDown(string slug)
        {
            if (config == null) return;
            var keys = config.Members.Keys.ToList();
            int index = keys.IndexOf(slug);
            if (index < 0 || index >= keys.Count - 1) return;

            var nextKey = keys[index + 1];
            var currentVal = config.Members[slug];
            var nextVal = config.Members[nextKey];

            var newDict = new Dictionary<string, User>();
            foreach (var key in keys)
            {
                if (key == slug) newDict[nextKey] = nextVal;
                else if (key == nextKey) newDict[slug] = currentVal;
                else newDict[key] = config.Members[key];
            }
            config.Members = newDict;
            openMenuMemberSlug = null;
        }

        private async Task ConfirmDeleteMember(string slug)
        {
            openMenuMemberSlug = null;
            bool confirm = await Alerts.ConfirmAsync(
                "Elimina Partecipante",
                $"Sei sicuro di voler eliminare {config?.Members[slug].Name}? Questa azione non può essere annullata.",
                "Elimina",
                "Annulla");

            if (confirm)
            {
                RemoveMember(slug);
            }
        }

        private async Task HandleSave()
        {
            if (config == null) return;
            
            // Trim all strings
            config.Name = config.Name?.Trim() ?? "";
            config.Description = config.Description?.Trim() ?? "";
            
            foreach (var curr in config.Currencies.Values)
            {
                curr.Symbol = curr.Symbol?.Trim() ?? "";
                curr.Name = curr.Name?.Trim() ?? "";
            }

            foreach (var member in config.Members.Values)
            {
                member.Name = member.Name?.Trim() ?? "";
                member.Email = member.Email?.Trim();
                if (string.IsNullOrWhiteSpace(member.Email)) member.Email = null;
            }

            if (string.IsNullOrWhiteSpace(config.Name)) { error = "Il nome è obbligatorio."; return; }
            if (config.Currencies.Count == 0) { error = "Aggiungi almeno una valuta."; return; }

            var settings = await Storage.GetAppSettingsAsync();
            await Storage.SaveTripConfigAsync(tripSlug, config, settings?.DeviceId ?? "unknown");
            Nav.NavigateTo($"/trip/{tripSlug}");
        }

        private async Task HandleDelete()
        {
            bool confirm = await Alerts.ConfirmAsync(
                "Elimina Viaggio",
                "Sei sicuro di voler eliminare questo viaggio dal dispositivo?",
                "Elimina",
                "Annulla");

            if (confirm)
            {
                await Storage.DeleteTripAsync(tripSlug);
                Nav.NavigateTo("/");
            }
        }
    }
}

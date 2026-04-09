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

        private void ToggleHeaderMenu() => isHeaderMenuOpen = !isHeaderMenuOpen;

        protected override async Task OnInitializedAsync()
        {
            config = await Storage.GetTripConfigAsync(tripSlug);
            var settings = await Storage.GetAppSettingsAsync();
            deviceId = settings?.DeviceId ?? "";
            authorName = settings?.AuthorName ?? "";
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

        private void AddMember()
        {
            if (config == null) return;
            if (string.IsNullOrWhiteSpace(newMemberSlug)) { error = "Lo slug è obbligatorio."; return; }
            
            // If not editing, check for duplicates
            if (editingMemberSlug == null && config.Members.ContainsKey(newMemberSlug))
            {
                error = "Partecipante già presente.";
                return;
            }

            config.Members[newMemberSlug] = new User
            {
                Name = newMemberName,
                Email = newMemberEmail,
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

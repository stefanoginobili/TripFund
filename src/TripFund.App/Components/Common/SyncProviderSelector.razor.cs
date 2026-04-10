using Microsoft.AspNetCore.Components;
using TripFund.App.Models;

namespace TripFund.App.Components.Common
{
    public partial class SyncProviderSelector
    {
        [Parameter] public bool IsVisible { get; set; }
        [Parameter] public bool IsJoining { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }
        [Parameter] public EventCallback<SyncProviderSelection> OnSelectionCompleted { get; set; }

        private string? selectedProvider;
        private string folderUrl = "";
        private string repoUrl = "";
        private string gitPat = "";

        private string GetTitle()
        {
            if (selectedProvider == "google-drive") return "Google Drive";
            if (selectedProvider == "git") return "Git";
            return "Seleziona Provider";
        }

        private void SelectProvider(string provider)
        {
            selectedProvider = provider;
        }

        private void Back()
        {
            selectedProvider = null;
        }

        private async Task Close()
        {
            selectedProvider = null;
            await OnClose.InvokeAsync();
        }

        private async Task Complete()
        {
            var parameters = new Dictionary<string, string>();
            if (selectedProvider == "google-drive")
            {
                parameters["folderUrl"] = folderUrl;
            }
            else if (selectedProvider == "git")
            {
                parameters["repository"] = repoUrl;
                parameters["pat"] = gitPat;
            }

            var selection = new SyncProviderSelection
            {
                Provider = selectedProvider ?? "",
                Parameters = parameters
            };

            await OnSelectionCompleted.InvokeAsync(selection);
            selectedProvider = null;
            folderUrl = "";
            repoUrl = "";
            gitPat = "";
        }
    }
}

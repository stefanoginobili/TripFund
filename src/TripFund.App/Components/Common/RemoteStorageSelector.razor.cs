using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.App.Components.Common
{
    public partial class RemoteStorageSelector
    {
        [Inject] private IGoogleAuthConfiguration GoogleConfig { get; set; } = default!;
        [Inject] private IMicrosoftAuthConfiguration MicrosoftConfig { get; set; } = default!;
        [Inject] private IRemoteStorageService RemoteStorageService { get; set; } = default!;
        [Inject] private IAlertService AlertService { get; set; } = default!;
        [Inject] private GoogleDriveRemoteStorageService GoogleDriveService { get; set; } = default!;
        [Inject] private OneDriveRemoteStorageService OneDriveService { get; set; } = default!;
        [Inject] private IGooglePickerService GooglePickerService { get; set; } = default!;

        [Parameter] public bool IsVisible { get; set; }
        [Parameter] public bool IsJoining { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }
        [Parameter] public EventCallback<RemoteStorageSelection?> OnSelectionCompleted { get; set; }

        private string? selectedProvider;
        private string folderId = "";
        private string folderName = "";
        private string? driveId;
        private bool isPickerLoading = false;

        private bool isOneDrivePickerVisible = false;
        private string oneDriveToken = "";

        private string GetTitle()
        {
            if (selectedProvider == "google-drive") return "Google Drive";
            if (selectedProvider == "onedrive") return "Microsoft OneDrive";
            if (selectedProvider == "local") return "Memoria Locale";
            return "Seleziona Archivio";
        }

        private async Task OpenPicker()
        {
            try
            {
                isPickerLoading = true;
                StateHasChanged();

                if (selectedProvider == "google-drive")
                {
                    var token = await GoogleDriveService.GetAccessTokenAsync();
                    if (string.IsNullOrEmpty(token))
                    {
                        await AlertService.ShowAlertAsync("Errore", "Impossibile autenticare l'account Google.");
                        return;
                    }

                    var title = IsJoining ? "Aggiungi viaggio esistente" : "Crea nuovo viaggio";
                    var result = await GooglePickerService.PickFolderAsync(GoogleConfig.GoogleAppId, token, GoogleConfig.GoogleApiKey, title);
                    
                    if (!string.IsNullOrEmpty(result.FolderId))
                    {
                        folderId = result.FolderId;
                        folderName = result.FolderName ?? "Cartella senza nome";
                    }
                }
                else if (selectedProvider == "onedrive")
                {
                    var token = await OneDriveService.GetAccessTokenAsync();
                    if (string.IsNullOrEmpty(token))
                    {
                        await AlertService.ShowAlertAsync("Errore", "Impossibile autenticare l'account Microsoft.");
                        return;
                    }

                    oneDriveToken = token;
                    isOneDrivePickerVisible = true;
                }
            }
            catch (Exception)
            {
                await AlertService.ShowAlertAsync("Errore", "Si è verificato un errore durante l'apertura del selettore.");
            }
            finally
            {
                isPickerLoading = false;
                StateHasChanged();
            }
        }

        private void OnOneDriveFolderSelected((string Id, string Name, string? DriveId) result)
        {
            folderId = result.Id;
            folderName = result.Name;
            driveId = result.DriveId;
            StateHasChanged();
        }

        private void CancelLoading()
        {
            isPickerLoading = false;
        }

        private void SelectProvider(string provider)
        {
            selectedProvider = provider;
            if (provider == "local")
            {
                _ = Complete();
            }
        }

        private void Back()
        {
            selectedProvider = null;
            folderId = "";
            folderName = "";
            driveId = null;
        }

        private async Task Close()
        {
            selectedProvider = null;
            folderId = "";
            folderName = "";
            driveId = null;
            await OnClose.InvokeAsync();
        }

        private async Task Complete()
        {
            if (selectedProvider == "local")
            {
                await OnSelectionCompleted.InvokeAsync(null);
                selectedProvider = null;
                return;
            }

            var parameters = new Dictionary<string, string>();
            if (selectedProvider == "google-drive" || selectedProvider == "onedrive")
            {
                parameters["folderId"] = folderId;
                parameters["folderName"] = folderName;
                if (!string.IsNullOrEmpty(driveId))
                {
                    parameters["driveId"] = driveId;
                }
            }

            var selection = new RemoteStorageSelection
            {
                Provider = selectedProvider ?? "",
                Parameters = parameters
            };

            await OnSelectionCompleted.InvokeAsync(selection);
            selectedProvider = null;
            folderId = "";
            folderName = "";
            driveId = null;
        }
    }
}

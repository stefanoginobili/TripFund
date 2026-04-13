using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.App.Components.Common
{
    public partial class RemoteStorageSelector
    {
        [Inject] private IMicrosoftAuthConfiguration MicrosoftConfig { get; set; } = default!;
        [Inject] private IRemoteStorageService RemoteStorageService { get; set; } = default!;
        [Inject] private IAlertService AlertService { get; set; } = default!;
        [Inject] private OneDriveRemoteStorageService OneDriveService { get; set; } = default!;

        [Parameter] public bool IsVisible { get; set; }
        [Parameter] public bool IsJoining { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }
        [Parameter] public EventCallback<RemoteStorageSelection?> OnSelectionCompleted { get; set; }

        private bool _wasVisible;

        protected override void OnParametersSet()
        {
            if (IsVisible && !_wasVisible)
            {
                ResetState();
            }
            _wasVisible = IsVisible;
        }

        private void ResetState()
        {
            selectedProvider = null;
            folderId = "";
            folderName = "";
            driveId = null;
            isPickerLoading = false;
            isOneDrivePickerVisible = false;
            oneDriveToken = "";
            oneDriveRefreshToken = null;
        }

        private string? selectedProvider;
        private string folderId = "";
        private string folderName = "";
        private string? driveId;
        private bool isPickerLoading = false;

        private bool isOneDrivePickerVisible = false;
        private string oneDriveToken = "";
        private string? oneDriveRefreshToken;

        private string GetTitle()
        {
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

                if (selectedProvider == "onedrive")
                {
                    var authResult = await OneDriveService.AuthenticateUserAsync();
                    if (authResult == null || !authResult.TryGetValue("accessToken", out var token))
                    {
                        await AlertService.ShowAlertAsync("Errore", "Impossibile autenticare l'account Microsoft.");
                        return;
                    }

                    oneDriveToken = token;
                    authResult.TryGetValue("refreshToken", out oneDriveRefreshToken);
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

        private async Task Close()
        {
            ResetState();
            await OnClose.InvokeAsync();
        }

        private async Task Complete()
        {
            if (selectedProvider == "local")
            {
                await OnSelectionCompleted.InvokeAsync(null);
                ResetState();
                return;
            }

            var parameters = new Dictionary<string, string>();
            if (selectedProvider == "onedrive")
            {
                parameters["folderId"] = folderId;
                parameters["folderName"] = folderName;
                if (!string.IsNullOrEmpty(driveId))
                {
                    parameters["driveId"] = driveId;
                }
                if (!string.IsNullOrEmpty(oneDriveRefreshToken))
                {
                    parameters["refreshToken"] = oneDriveRefreshToken;
                }
            }

            var selection = new RemoteStorageSelection
            {
                Provider = selectedProvider ?? "",
                Parameters = parameters
            };

            await OnSelectionCompleted.InvokeAsync(selection);
            ResetState();
        }
    }
}

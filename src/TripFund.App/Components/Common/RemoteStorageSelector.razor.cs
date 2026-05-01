using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Common
{
    public partial class RemoteStorageSelector : IDisposable
    {
        [Inject] private IAlertService AlertService { get; set; } = default!;
        [Inject] private OneDriveRemoteStorageService OneDriveService { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;

        [Parameter] public bool IsVisible { get; set; }
        [Parameter] public bool IsJoining { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }
        [Parameter] public EventCallback<RemoteStorageSelection?> OnSelectionCompleted { get; set; }

        private bool _wasVisible;
        private CancellationTokenSource? _cts;

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

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
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            selectedProvider = null;
            folderId = "";
            folderName = "";
            driveId = null;
            isPickerLoading = false;
            isPastingLink = false;
            sharedLinkUrl = "";
            linkResolveError = null;
            isResolvingLink = false;
            isOneDrivePickerVisible = false;
            oneDriveToken = "";
            oneDriveRefreshToken = null;
        }

        private string? selectedProvider;
        private string folderId = "";
        private string folderName = "";
        private string? driveId;
        private bool isPickerLoading = false;

        private bool isPastingLink = false;
        private string sharedLinkUrl = "";
        private bool isResolvingLink = false;
        private string? linkResolveError;
        private ElementReference sharedLinkInput;

        private async Task SelectAllText()
        {
            await JS.InvokeVoidAsync("selectElementText", sharedLinkInput);
        }

        private void TrimSharedLink()
        {
            sharedLinkUrl = sharedLinkUrl?.Trim() ?? string.Empty;
        }

        private bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private bool isOneDrivePickerVisible = false;
        private string oneDriveToken = "";
        private string? oneDriveRefreshToken;

        private string GetTitle()
        {
            if (selectedProvider == "onedrive") return "Microsoft OneDrive";
            if (selectedProvider == "local") return "Memoria Locale";
            return "Seleziona Archivio";
        }

        private async Task OnConfirmLink()
        {
            try
            {
                sharedLinkUrl = sharedLinkUrl?.Trim() ?? string.Empty;
                linkResolveError = null;
                isResolvingLink = true;
                
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                StateHasChanged();

                if (string.IsNullOrWhiteSpace(sharedLinkUrl))
                {
                    linkResolveError = "Inserisci un link valido.";
                    return;
                }

                // 1. Authenticate first (Graph API requires a token even for shared links)
                var authResult = await OneDriveService.AuthenticateUserAsync(token);
                
                // If we were cancelled while waiting for the browser, stop here
                if (token.IsCancellationRequested) return;

                if (authResult == null || !authResult.TryGetValue("accessToken", out var accessToken))
                {
                    linkResolveError = "Autenticazione Microsoft fallita.";
                    return;
                }

                oneDriveToken = accessToken;
                authResult.TryGetValue("refreshToken", out oneDriveRefreshToken);

                // 2. Resolve the link
                var resolved = await OneDriveService.ResolveSharedLinkAsync(sharedLinkUrl, accessToken);
                if (resolved == null)
                {
                    linkResolveError = "Impossibile risolvere il link. Assicurati che sia un link di condivisione OneDrive valido.";
                    return;
                }

                folderId = resolved.Value.FolderId;
                folderName = resolved.Value.Name;
                driveId = resolved.Value.DriveId;
                
                // Finalize the selection automatically after successful resolution
                await Complete();
            }
            catch (OperationCanceledException)
            {
                linkResolveError = null; // Simply stop resolving without error
            }
            catch (Exception ex)
            {
                linkResolveError = "Si è verificato un errore durante la verifica del link.";
                TripFundLogger.Error("Error resolving link", ex);
            }
            finally
            {
                isResolvingLink = false;
                StateHasChanged();
            }
        }

        private async Task OpenPicker()
        {
            if (isPickerLoading) return;

            try
            {
                isPickerLoading = true;
                
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                StateHasChanged();

                if (selectedProvider == "onedrive")
                {
                    var authResult = await OneDriveService.AuthenticateUserAsync(token);
                    
                    // If we were cancelled while waiting for the browser, stop here
                    if (token.IsCancellationRequested) return;

                    if (authResult == null || !authResult.TryGetValue("accessToken", out var accessToken))
                    {
                        await AlertService.ShowAlertAsync("Errore", "Impossibile autenticare l'account Microsoft.", type: AlertType.Error);
                        return;
                    }

                    oneDriveToken = accessToken;
                    authResult.TryGetValue("refreshToken", out oneDriveRefreshToken);
                    isOneDrivePickerVisible = true;
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled the flow, no need to show an error
            }
            catch (Exception ex)
            {
                TripFundLogger.Error("Error opening OneDrive picker", ex);
                await AlertService.ShowAlertAsync("Errore", "Si è verificato un errore durante l'apertura del selettore.", type: AlertType.Error);
            }
            finally
            {
                isPickerLoading = false;
                StateHasChanged();
            }
        }

        private async Task OnOneDriveFolderSelected((string Id, string Name, string? DriveId) result)
        {
            folderId = result.Id;
            folderName = result.Name;
            driveId = result.DriveId;
            StateHasChanged();
            
            // Automatically complete the selection to remove the extra "Conferma" click
            await Complete();
        }

        private void CancelLoading()
        {
            _cts?.Cancel();
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
                Parameters = parameters,
                IsReadonly = false
            };

            await OnSelectionCompleted.InvokeAsync(selection);
            ResetState();
        }
    }
}

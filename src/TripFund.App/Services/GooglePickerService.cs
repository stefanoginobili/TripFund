using System;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Maui.Authentication;

namespace TripFund.App.Services;

/// <summary>
/// Implementation of IGooglePickerService using WebAuthenticator to interact with an external static HTML page.
/// </summary>
public class GooglePickerService : IGooglePickerService
{
    private const string PickerUrl = "https://tripfund-31d41.web.app/picker/index.html";

    public async Task<(string? FolderId, string? FolderName)> PickFolderAsync(string appId, string accessToken, string apiKey, string title)
    {
        try
        {
            // Determine the callback scheme based on current app configuration (debug/release)
            // This should match what's in AndroidManifest.xml and Info.plist
            var callbackUrl = $"{AppInfo.Current.PackageName}://picker-callback";

            // 1. Build the URL with query parameters for the external picker
            var builder = new UriBuilder(PickerUrl);
            var query = HttpUtility.ParseQueryString(builder.Query);
            query["token"] = accessToken;
            query["key"] = apiKey;
            query["scheme"] = callbackUrl;
            query["title"] = title;
            query["appId"] = appId;
            builder.Query = query.ToString();

            // 2. Launch the WebAuthenticator
            var authResult = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = builder.Uri,
                CallbackUrl = new Uri(callbackUrl)
            });

            // 3. Extract the result from the redirect query string
            if (authResult != null)
            {
                authResult.Properties.TryGetValue("folderId", out var folderId);
                authResult.Properties.TryGetValue("folderName", out var folderName);

                return (folderId, folderName);
            }
        }
        catch (TaskCanceledException)
        {
            // User closed the browser or cancelled the operation
        }
        catch (Exception ex)
        {
            // Log exception as needed
            Console.WriteLine($"[GooglePickerService] Error: {ex.Message}");
        }

        return (null, null);
    }
}

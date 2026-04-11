namespace TripFund.App.Services;

/// <summary>
/// Interface for picking a Google Drive folder using an external Picker service to bypass WebView cookie restrictions.
/// </summary>
public interface IGooglePickerService
{
    /// <summary>
    /// Launches the system browser to allow the user to select a Google Drive folder.
    /// </summary>
    /// <param name="appId">Google Cloud Project ID.</param>
    /// <param name="accessToken">Valid Google OAuth2 Access Token.</param>
    /// <param name="apiKey">Google API Key.</param>
    /// <param name="title">The picker's title.</param>
    /// <returns>A tuple containing the folder ID and folder Name if successful; otherwise null values.</returns>
    Task<(string? FolderId, string? FolderName)> PickFolderAsync(string appId, string accessToken, string apiKey, string title);
}

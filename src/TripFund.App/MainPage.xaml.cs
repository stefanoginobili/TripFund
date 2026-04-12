using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TripFund.App;

public partial class MainPage : ContentPage
{
    private readonly IServiceProvider _services;

	public MainPage(IServiceProvider services)
	{
		InitializeComponent();
        _services = services;
	}

    protected override bool OnBackButtonPressed()
    {
        var nav = _services.GetRequiredService<NavigationManager>();
        // ToBaseRelativePath returns "" for the base URI (home)
        string relativePath = nav.ToBaseRelativePath(nav.Uri);

        // Remove any query parameters or fragments for the check
        int queryIndex = relativePath.IndexOf('?');
        if (queryIndex != -1) relativePath = relativePath.Substring(0, queryIndex);
        int fragmentIndex = relativePath.IndexOf('#');
        if (fragmentIndex != -1) relativePath = relativePath.Substring(0, fragmentIndex);

        // Debug: using relativePath check
        bool isRoot = string.IsNullOrEmpty(relativePath) || 
                      relativePath == "/" || 
                      relativePath == "onboarding" || 
                      relativePath == "index.html";

        if (isRoot)
        {
            // For root pages, we want to exit the app.
            // On Android, returning false should trigger the default Activity.OnBackPressed
            // which finishes the activity. Calling Quit() makes it more explicit.
            Application.Current?.Quit();
            return false;
        }

        // For other pages, go back in history.
        var js = _services.GetRequiredService<IJSRuntime>();
        js.InvokeVoidAsync("history.back");
        return true;
    }
}

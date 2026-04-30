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
        try
        {
            var nav = _services.GetService<NavigationManager>();
            if (nav == null) return false;

            string relativePath = nav.ToBaseRelativePath(nav.Uri);

            // Remove fragment
            int fragmentIndex = relativePath.IndexOf('#');
            if (fragmentIndex != -1) relativePath = relativePath.Substring(0, fragmentIndex);

            // Parse path
            string path = relativePath;
            int queryIndex = relativePath.IndexOf('?');
            if (queryIndex != -1) path = relativePath.Substring(0, queryIndex);

            path = path.Trim('/');

            bool isRoot = string.IsNullOrEmpty(path) || 
                          path == "onboarding" || 
                          path == "not-found" ||
                          path == "index.html";

            if (isRoot)
            {
                return false;
            }

            // Calculate parent
            string? parentUrl = GetParentUrl(nav.Uri);
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (parentUrl != null)
                    {
                        nav.NavigateTo(parentUrl);
                    }
                    else
                    {
                        nav.NavigateTo("/");
                    }
                }
                catch
                {
                    // Ignore navigation errors on shutdown
                }
            });

            return true;
        }
        catch
        {
            // If anything goes wrong (e.g. NavigationManager disposed), 
            // return false to let the OS handle it safely.
            return false;
        }
    }

    private string? GetParentUrl(string absoluteUri)
    {
        try
        {
            var nav = _services.GetService<NavigationManager>();
            if (nav == null) return null;

            var uri = new Uri(absoluteUri);
            string relativePath = nav.ToBaseRelativePath(absoluteUri);

            int fragmentIndex = relativePath.IndexOf('#');
            if (fragmentIndex != -1) relativePath = relativePath.Substring(0, fragmentIndex);

            string path = relativePath;
            int queryIndex = relativePath.IndexOf('?');
            if (queryIndex != -1) path = relativePath.Substring(0, queryIndex);

            path = path.Trim('/');
            if (string.IsNullOrEmpty(path) || path == "onboarding" || path == "not-found" || path == "index.html")
                return null;

            if (path == "settings" || path == "create-trip")
                return "/";

            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            var segments = path.Split('/');

            if (segments.Length >= 2 && segments[0] == "trip")
            {
                string tripSlug = segments[1];
                if (segments.Length == 2) return "/";

                if (segments.Length == 3 && segments[2] == "edit")
                    return $"/trip/{tripSlug}";

                if (segments.Length == 3 && segments[2] == "charts")
                    return $"/trip/{tripSlug}";

                if (segments.Length == 3 && (segments[2] == "expense" || segments[2] == "contribution"))
                {
                    string? member = query.TryGetValue("member", out var m) ? m.ToString() : null;
                    string? currency = query.TryGetValue("currency", out var c) ? c.ToString() : null;
                    string queryStr = !string.IsNullOrEmpty(currency) ? $"?currency={currency}" : "";

                    if (!string.IsNullOrEmpty(member))
                        return $"/trip/{tripSlug}/member/{member}{queryStr}";
                    
                    return $"/trip/{tripSlug}{queryStr}";
                }

                if (segments.Length == 4 && segments[2] == "member")
                {
                    string? currency = query.TryGetValue("currency", out var c) ? c.ToString() : null;
                    string queryStr = !string.IsNullOrEmpty(currency) ? $"?currency={currency}" : "";
                    return $"/trip/{tripSlug}{queryStr}";
                }
            }

            return "/";
        }
        catch
        {
            return "/";
        }
    }
}

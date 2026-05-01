using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TripFund.App.Utilities;

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
        var navService = _services.GetService<Services.INavigationService>();
        if (navService == null) return false;

        TripFundLogger.Debug($"[MainPage] OnBackButtonPressed: StackCount={navService.StackCount}, HasBeforeNavigateAction={navService.HasBeforeNavigateAction}");

        // If the stack is empty and no action is registered, we return false to let the OS exit the app.
        if (navService.StackCount == 0 && !navService.HasBeforeNavigateAction)
        {
            TripFundLogger.Debug("[MainPage] OnBackButtonPressed: Stack empty and no action, letting OS handle back (exit app)");
            return false;
        }

        // Fire and forget the async back navigation
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await navService.GoBackAsync();
            }
            catch (Exception ex)
            {
                TripFundLogger.Error("[MainPage] Error in GoBackAsync", ex);
            }
        });

        return true;
    }
}

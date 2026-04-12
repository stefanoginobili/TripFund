using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace TripFund.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var itCulture = new System.Globalization.CultureInfo("it-IT");
		System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
		System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;
		System.Globalization.CultureInfo.CurrentCulture = itCulture;
		System.Globalization.CultureInfo.CurrentUICulture = itCulture;

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddHttpClient(nameof(Services.OneDriveRemoteStorageService));
		builder.Services.AddSingleton<Services.OneDriveRemoteStorageService>();
		builder.Services.AddHttpClient<Services.IOneDrivePickerService, Services.OneDrivePickerService>();
		builder.Services.AddSingleton<Services.LocalTripStorageService>();
		builder.Services.AddSingleton<Services.RemoteStorageSyncEngine>();
		builder.Services.AddSingleton<Services.IMicrosoftAuthConfiguration, Services.MicrosoftAuthConfiguration>();
		builder.Services.AddSingleton<Services.IWebAuthenticator, Services.MauiWebAuthenticator>();
		builder.Services.AddSingleton<Services.IRemoteStorageService, Services.CompositeRemoteStorageService>();
		builder.Services.AddSingleton<Services.IAlertService, Services.MauiAlertService>();
		builder.Services.AddSingleton<Services.IThumbnailService, Services.ThumbnailService>();
		builder.Services.AddSingleton<Services.IEmailService, Services.EmailService>();

#if ANDROID
		builder.Services.AddSingleton<Services.INativeDatePickerService, Platforms.Android.NativeDatePickerService>();
#else
		builder.Services.AddSingleton<Services.INativeDatePickerService, Services.NativeDatePickerService>();
#endif

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

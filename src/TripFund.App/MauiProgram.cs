using Microsoft.Extensions.Logging;

namespace TripFund.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var itCulture = new System.Globalization.CultureInfo("it-IT");
		System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
		System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<Services.LocalTripStorageService>();
		builder.Services.AddSingleton<Services.IDriveService, Services.MockDriveService>();
		builder.Services.AddSingleton<Services.IAlertService, Services.MauiAlertService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

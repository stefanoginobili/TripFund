namespace TripFund.App;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
		_services = services;

		// Preload timezones in background
		_ = Utilities.TimeZoneMapper.PreloadAsync();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage(_services)) { Title = "TripFund.App" };
	}
}

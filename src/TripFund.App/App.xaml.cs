namespace TripFund.App;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage(_services)) { Title = "TripFund.App" };
	}
}

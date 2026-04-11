using Android.App;
using Android.Runtime;
using Java.Util;

namespace TripFund.App;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	public override void OnCreate()
	{
		var locale = new Java.Util.Locale("it", "IT");
		Java.Util.Locale.Default = locale;

		var config = new Android.Content.Res.Configuration();
		config.SetLocale(locale);

		#pragma warning disable CA1422
		if (Resources != null)
		{
			Resources.UpdateConfiguration(config, Resources.DisplayMetrics);
		}
		#pragma warning restore CA1422

		base.OnCreate();
		}


	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

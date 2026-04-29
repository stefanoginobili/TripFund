using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
using AndroidX.Core.OS;

namespace TripFund.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void AttachBaseContext(Context? @base)
    {
        var locale = new Java.Util.Locale("it", "IT");
        Java.Util.Locale.Default = locale;

        var config = new Android.Content.Res.Configuration(@base?.Resources?.Configuration);
        config.SetLocale(locale);

        var context = @base?.CreateConfigurationContext(config);
        base.AttachBaseContext(context);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Force it-IT locale for system pickers and WebView native controls
        var locale = new Java.Util.Locale("it", "IT");
        Java.Util.Locale.Default = locale;

        var appLocale = LocaleListCompat.ForLanguageTags("it-IT");
        if (appLocale != null)
        {
            AppCompatDelegate.ApplicationLocales = appLocale;
        }

        base.OnCreate(savedInstanceState);

        // Ensure configuration is updated for current resources
#pragma warning disable CA1422
        if (Resources?.Configuration != null)
        {
            Resources.Configuration.SetLocale(locale);
            Resources.UpdateConfiguration(Resources.Configuration, Resources.DisplayMetrics);
        }
#pragma warning restore CA1422

        AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
        if (Window != null)
        {
            WindowCompat.SetDecorFitsSystemWindows(Window, false);

#pragma warning disable CA1422
            Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#F8FBF9"));
            Window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
#pragma warning restore CA1422

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                Window.NavigationBarContrastEnforced = false;
            }

            if (Window.DecorView != null)
            {
                var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
                if (controller != null)
                {
                    controller.AppearanceLightStatusBars = true;
                    controller.AppearanceLightNavigationBars = true;
                }
            }
        }
    }
}

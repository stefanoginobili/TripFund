using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;

namespace TripFund.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density, WindowSoftInputMode = Android.Views.SoftInput.AdjustResize)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
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

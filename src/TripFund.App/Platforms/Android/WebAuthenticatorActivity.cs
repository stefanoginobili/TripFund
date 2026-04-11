using Android.App;
using Android.Content;
using Android.Content.PM;

namespace TripFund.App.Platforms.Android;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView },
              Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
              DataScheme = WebAuthenticatorActivity.DataScheme)]
public class WebAuthenticatorActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
#if DEBUG
    public const string DataScheme = "com.stefanoginobili.tripfund.app.dev";
#else
    public const string DataScheme = "com.stefanoginobili.tripfund.app";
#endif
}

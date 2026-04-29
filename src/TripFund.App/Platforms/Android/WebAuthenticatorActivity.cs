using Android.App;
using Android.Content;
using Android.Content.PM;

namespace TripFund.App.Platforms.Android;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView },
              Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
              DataScheme = DataScheme)]
[IntentFilter(new[] { Intent.ActionView },
              Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
              DataScheme = DataSchemeMicrosoft)]
public class WebAuthenticatorActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
#if DEBUG
    public const string DataScheme = "com.stefanoginobili.tripfund.app.dev";
    public const string DataSchemeMicrosoft = "msal4197af94-b80a-4862-8743-835a14955fd1";
#else
    public const string DataScheme = "com.stefanoginobili.tripfund.app";
    public const string DataSchemeMicrosoft = "msal4197af94-b80a-4862-8743-835a14955fd1";
#endif
}

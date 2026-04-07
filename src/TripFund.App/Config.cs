namespace TripFund.App;

public static class Config
{
#if ANDROID
#if DEBUG
    public const string GoogleClientId = "569148799336-25otr16hil7nt5pa7oseh547ksrisliv.apps.googleusercontent.com";
#else
    public const string GoogleClientId = "PASTE_YOUR_ANDROID_RELEASE_CLIENT_ID_HERE";
#endif
#elif IOS
    public const string GoogleClientId = "569148799336-2elrhgtk8s1q2trll6s68bd0kphmgksq.apps.googleusercontent.com";
#else
    // Default or test fallback
    public const string GoogleClientId = null!;
#endif
}

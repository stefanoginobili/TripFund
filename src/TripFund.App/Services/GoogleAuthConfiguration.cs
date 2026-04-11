namespace TripFund.App.Services;

public interface IGoogleAuthConfiguration
{
    string GoogleClientId { get; }
}

public class GoogleAuthConfiguration : IGoogleAuthConfiguration
{
    public string GoogleClientId
    {
        get
        {
#if ANDROID
#if DEBUG
            return "ANDROID_CLIENT_ID_DEV";
#else
            return "ANDROID_CLIENT_ID_PROD";
#endif
#elif IOS
#if DEBUG
            return "IOS_CLIENT_ID_DEV";
#else
            return "IOS_CLIENT_ID_PROD";
#endif
#else
            return string.Empty;
#endif
        }
    }
}

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
            return "980516715181-o2u5ona8nnh2v46l1spbm7i854bujp8v.apps.googleusercontent.com";
#else
            return "ANDROID_CLIENT_ID_PROD";
#endif
#elif IOS
#if DEBUG
            return "980516715181-b4g5jlbe72uak9518dm1ej9h2l844038.apps.googleusercontent.com";
#else
            return "IOS_CLIENT_ID_PROD";
#endif
#else
            return string.Empty;
#endif
        }
    }
}

namespace TripFund.App.Services;

public interface IMicrosoftAuthConfiguration
{
    string MicrosoftClientId { get; }
    string MicrosoftTenantId { get; }
}

public class MicrosoftAuthConfiguration : IMicrosoftAuthConfiguration
{
    public string MicrosoftTenantId => "common";

    public string MicrosoftClientId
    {
        get
        {
#if ANDROID
#if DEBUG
            // Placeholder: Replace with your actual Android Client ID from Azure Portal
            return "4197af94-b80a-4862-8743-835a14955fd1";
#else
            return "4197af94-b80a-4862-8743-835a14955fd1";
#endif
#elif IOS
#if DEBUG
            // Placeholder: Replace with your actual iOS Client ID from Azure Portal
            return "4197af94-b80a-4862-8743-835a14955fd1";
#else
            return "4197af94-b80a-4862-8743-835a14955fd1";
#endif
#else
            return string.Empty;
#endif
        }
    }
}

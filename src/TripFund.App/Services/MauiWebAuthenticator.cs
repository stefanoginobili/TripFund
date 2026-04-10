namespace TripFund.App.Services;

public class MauiWebAuthenticator : IWebAuthenticator
{
    public Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions options)
    {
        return WebAuthenticator.Default.AuthenticateAsync(options);
    }
}

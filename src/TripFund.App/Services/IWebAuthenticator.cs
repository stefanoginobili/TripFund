namespace TripFund.App.Services;

public interface IWebAuthenticator
{
    Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions options, CancellationToken cancellationToken = default);
}

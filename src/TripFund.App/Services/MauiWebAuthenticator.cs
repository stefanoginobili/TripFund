namespace TripFund.App.Services;

public class MauiWebAuthenticator : IWebAuthenticator
{
    public async Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions options, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        var authTask = WebAuthenticator.Default.AuthenticateAsync(options);
        
        if (cancellationToken == default || !cancellationToken.CanBeCanceled)
            return await authTask;

        var tcs = new TaskCompletionSource<WebAuthenticatorResult>();
        using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
        {
            var completedTask = await Task.WhenAny(authTask, tcs.Task);
            return await completedTask; // This will throw if it's the cancellation task or return the result
        }
    }
}

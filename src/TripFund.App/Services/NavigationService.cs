using Microsoft.AspNetCore.Components;
using System.Diagnostics;

namespace TripFund.App.Services;

public interface INavigationService
{
    void Register(NavigationManager navigationManager);
    Task NavigateAsync(string fromUrl, string toUrl);
    Task<bool> GoBackAsync();
    void SetBeforeNavigateAction(Func<Task<bool>> action);
    void ClearBeforeNavigateAction();
    int StackCount { get; }
    bool HasBeforeNavigateAction { get; }
}

public class NavigationService : INavigationService
{
    private NavigationManager? _navigationManager;
    private readonly Stack<string> _historyStack = new();
    private Func<Task<bool>>? _beforeNavigateAction;

    public int StackCount => _historyStack.Count;
    public bool HasBeforeNavigateAction => _beforeNavigateAction != null;

    public void Register(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    private NavigationManager GetNavigationManager()
    {
        if (_navigationManager == null)
        {
            throw new InvalidOperationException("NavigationService has not been initialized with a NavigationManager. Ensure Register() is called during app startup (e.g., in Routes.razor).");
        }
        return _navigationManager;
    }

    public async Task NavigateAsync(string fromUrl, string toUrl)
    {
        Debug.WriteLine($"[NavService] NavigateAsync: from='{fromUrl}' to='{toUrl}'");
        
        if (_beforeNavigateAction != null)
        {
            var canProceed = await _beforeNavigateAction();
            if (!canProceed) 
            {
                Debug.WriteLine("[NavService] NavigateAsync: Vetoed by BeforeNavigateAction");
                return;
            }
        }

        if (fromUrl == toUrl)
        {
             Debug.WriteLine("[NavService] NavigateAsync: Ignored push because fromUrl == toUrl");
        }
        else if (!string.IsNullOrEmpty(fromUrl))
        {
            if (_historyStack.Count == 0 || _historyStack.Peek() != fromUrl)
            {
                _historyStack.Push(fromUrl);
                Debug.WriteLine($"[NavService] NavigateAsync: Pushed '{fromUrl}'. StackCount={_historyStack.Count}");
            }
        }
        
        GetNavigationManager().NavigateTo(toUrl);
    }

    public async Task<bool> GoBackAsync()
    {
        Debug.WriteLine($"[NavService] GoBackAsync: StackCount={_historyStack.Count}");
        
        if (_beforeNavigateAction != null)
        {
            var canProceed = await _beforeNavigateAction();
            if (!canProceed)
            {
                Debug.WriteLine("[NavService] GoBackAsync: Vetoed by BeforeNavigateAction");
                return true; // We handled it by staying
            }
        }

        if (_historyStack.Count == 0)
        {
            Debug.WriteLine("[NavService] GoBackAsync: Stack empty, returning false");
            return false; // Signaling to exit the app
        }

        var targetUrl = _historyStack.Pop();
        Debug.WriteLine($"[NavService] GoBackAsync: Popped '{targetUrl}'. Navigating (replace=true). StackCount={_historyStack.Count}");
        
        GetNavigationManager().NavigateTo(targetUrl, replace: true);
        return true;
    }

    public void SetBeforeNavigateAction(Func<Task<bool>> action)
    {
        _beforeNavigateAction = action;
    }

    public void ClearBeforeNavigateAction()
    {
        _beforeNavigateAction = null;
    }
}

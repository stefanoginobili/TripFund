using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TripFund.App.Components.Common;

public partial class SortableList<TItem> : ComponentBase, IAsyncDisposable
{
    [Parameter] public IEnumerable<TItem> Items { get; set; } = default!;
    [Parameter] public RenderFragment<TItem> ItemTemplate { get; set; } = default!;
    [Parameter] public RenderFragment<TItem> MenuTemplate { get; set; } = default!;
    [Parameter] public Func<TItem, object> KeySelector { get; set; } = i => i!;
    [Parameter] public bool IsReadonly { get; set; }
    [Parameter] public EventCallback<(int oldIndex, int newIndex)> OnReorder { get; set; }

    private ElementReference listElement;
    private DotNetObjectReference<SortableList<TItem>>? selfReference;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !IsReadonly)
        {
            selfReference = DotNetObjectReference.Create(this);
            await JSRuntime.InvokeVoidAsync("appLogic.initSortable", listElement, selfReference, ".drag-handle");
        }
    }

    [JSInvokable]
    public async Task OnReorderInternal(int oldIndex, int newIndex)
    {
        await OnReorder.InvokeAsync((oldIndex, newIndex));
    }

    public async ValueTask DisposeAsync()
    {
        if (selfReference != null)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("appLogic.destroySortable", listElement);
            }
            catch (JSDisconnectedException) { }
            finally
            {
                selfReference.Dispose();
            }
        }
    }
}

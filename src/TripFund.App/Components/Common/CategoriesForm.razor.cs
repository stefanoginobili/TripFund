using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Common;

public partial class CategoriesForm
{
    [Parameter] public Dictionary<string, ExpenseCategory> Categories { get; set; } = new();
    [Parameter] public EventCallback<Dictionary<string, ExpenseCategory>> CategoriesChanged { get; set; }
    [Parameter] public bool IsReadonly { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    private bool isAdding = false;
    private string? editingSlug = null;
    private bool _shouldScroll = false;
    private string newCategoryName = "";
    private string newCategorySlug = "";
    private string newCategoryIcon = "🚌";
    private string newCategoryColor = "#F8B195";
    private string error = "";

    private readonly string[] expenseEmojis = {
        "🏨", "🏘️", "🏠", "⛺", "🏕️", "🍝", "🍕", "🍔", "🍣", "🥗", 
        "🥖", "🥞", "🍖", "🥦", "🍎", "🍩", "🍫", "🍿", "🍬", "☕", 
        "🍹", "🍺", "🍷", "🥤", "🍦", "🚌", "🚂", "✈️", "🚗", "🚕", 
        "🚲", "🛵", "⛽", "🎫", "🗺️", "🏛️", "🎡", "🎭", "🎬", "🏟️", 
        "🛍️", "🎁", "👕", "👗", "👞", "🧢", "💍", "💄", "💼", "🎒", 
        "📷", "📱", "💻", "🔋", "🔌", "💸", "💰", "💳", "🏥", "💊"
    };

    private readonly string[] palette = {
        "#A8D8EA", "#AA96DA", "#FCBAD3", "#FFFFD2", "#DBE2EF", "#DCEDC1",
        "#FFD3B6", "#FFAAA5", "#FF8B94", "#D6EDE2", "#F9F7F7", "#EEEEEE",
        "#B2E2F2", "#C5E3F6", "#D1D1F7", "#E2D1F9", "#F9D1F3", "#F9D1D1",
        "#F9E6D1", "#F9F3D1", "#E6F9D1", "#D1F9D1", "#D1F9E6", "#D1F9F3",
        "#E0F2F1", "#F1F8E9", "#FFFDE7", "#FFF3E0", "#FBE9E7", "#EFEBE9",
        "#FAFAFA", "#ECEFF1", "#CFD8DC", "#B0BEC5", "#90A4AE", "#78909C"
    };

    private void OnNameInput(ChangeEventArgs e)
    {
        newCategoryName = e.Value?.ToString() ?? "";
        newCategorySlug = SlugUtility.GenerateSlug(newCategoryName);
    }

    private void SelectEmoji(string emoji) => newCategoryIcon = emoji;
    private void SelectColor(string color) => newCategoryColor = color;

    private void StartAdd()
    {
        editingSlug = null;
        ResetForm();
        isAdding = true;
        _shouldScroll = true;
    }

    private async Task StartEdit(string slug, ExpenseCategory category)
    {
        editingSlug = slug;
        newCategoryName = category.Name;
        newCategoryIcon = category.Icon;
        newCategoryColor = category.Color;
        isAdding = false;
        _shouldScroll = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldScroll)
        {
            _shouldScroll = false;
            if (isAdding)
            {
                await JSRuntime.InvokeVoidAsync("appLogic.scrollIntoView", "#new-category-form", "center");
            }
            else if (editingSlug != null)
            {
                await JSRuntime.InvokeVoidAsync("appLogic.scrollIntoView", "#edit-category-form", "center");
            }
        }
    }

    private void CancelEdit()
    {
        editingSlug = null;
        ResetForm();
    }

    private void ResetForm()
    {
        newCategoryName = "";
        newCategorySlug = "";
        newCategoryIcon = "🚌";
        newCategoryColor = "#F8B195";
        error = "";
    }

    private async Task SaveCategory()
    {
        if (string.IsNullOrWhiteSpace(newCategoryName)) { error = "Il nome è obbligatorio."; return; }
        
        string slugToUse = editingSlug ?? newCategorySlug;
        if (string.IsNullOrWhiteSpace(slugToUse)) { error = "Lo slug è obbligatorio."; return; }

        if (editingSlug == null && Categories.ContainsKey(slugToUse))
        {
            error = "Questa categoria esiste già.";
            return;
        }

        var newCat = new ExpenseCategory
        {
            Name = newCategoryName.Trim(),
            Icon = newCategoryIcon,
            Color = newCategoryColor
        };

        if (editingSlug != null)
        {
            Categories[editingSlug] = newCat;
        }
        else
        {
            Categories[slugToUse] = newCat;
        }

        editingSlug = null;
        isAdding = false;
        ResetForm();
        await CategoriesChanged.InvokeAsync(Categories);
    }

    private async Task DeleteCategory(string slug)
    {
        Categories.Remove(slug);
        await CategoriesChanged.InvokeAsync(Categories);
    }

    private async Task MoveUp(string slug)
    {
        var keys = Categories.Keys.ToList();
        int index = keys.IndexOf(slug);
        if (index > 0)
        {
            var prevKey = keys[index - 1];
            var newDict = new Dictionary<string, ExpenseCategory>();
            foreach (var key in keys)
            {
                if (key == prevKey) newDict[slug] = Categories[slug];
                else if (key == slug) newDict[prevKey] = Categories[prevKey];
                else newDict[key] = Categories[key];
            }
            Categories = newDict;
            await CategoriesChanged.InvokeAsync(Categories);
        }
    }

    private async Task MoveDown(string slug)
    {
        var keys = Categories.Keys.ToList();
        int index = keys.IndexOf(slug);
        if (index < keys.Count - 1)
        {
            var nextKey = keys[index + 1];
            var newDict = new Dictionary<string, ExpenseCategory>();
            foreach (var key in keys)
            {
                if (key == slug) newDict[nextKey] = Categories[nextKey];
                else if (key == nextKey) newDict[slug] = Categories[slug];
                else newDict[key] = Categories[key];
            }
            Categories = newDict;
            await CategoriesChanged.InvokeAsync(Categories);
        }
    }
}

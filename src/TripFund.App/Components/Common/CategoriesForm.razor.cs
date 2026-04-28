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
    private string newCategoryColor = "#FF0000";
    private string error = "";

    private readonly string[] expenseEmojis = {
        "👛", "🫙", "💸", "🪙", "🧾", "💳", "✈️", "🚂", "🚌", "🚗",
        "🚕", "⛽", "🅿️", "🎫", "🚲", "🛳️", "🏨", "🏠", "🛌", "⛺",
        "🔑", "🍴", "🍕", "🍔", "🍱", "🍝", "🛒", "☕", "🥐", "🍦",
        "🍺", "🍷", "🍸", "🥤", "🧂", "🎟️", "🏰", "🏛️", "🎡", "🎭",
        "🏟️", "🏖️", "🏔️", "🛶", "🧖", "🛍️", "🎁", "📸", "📱", "📶",
        "🔋", "💊", "🧴", "🧺", "🚽", "🩹", "☂️", "🎒", "✨", "📦"
    };

    private readonly string[] palette = {
        "#FF0000", "#FF4500", "#FF8C00", "#FFA500", "#FFD700", "#FFFF00",
        "#9ACD32", "#32CD32", "#00FF00", "#00FA9A", "#00FFFF", "#00BFFF",
        "#0000FF", "#00008B", "#8A2BE2", "#4B0082", "#800080", "#FF00FF",
        "#FF1493", "#DC143C", "#A52A2A", "#800000", "#808000", "#008000",
        "#008080", "#000080", "#B22222", "#D2691E", "#FF6347", "#FF69B4",
        "#DA70D6", "#9932CC", "#4169E1", "#1E90FF", "#20B2AA", "#2E8B57"
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

    private void TrimName()
    {
        newCategoryName = newCategoryName?.Trim() ?? "";
    }

    private void TrimSlug()
    {
        newCategorySlug = newCategorySlug?.Trim() ?? "";
    }

    private async Task SaveCategory()
    {
        newCategoryName = newCategoryName.Trim();
        newCategorySlug = newCategorySlug.Trim();

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
            Name = newCategoryName,
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

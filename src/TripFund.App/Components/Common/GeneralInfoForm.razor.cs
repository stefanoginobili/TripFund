using Microsoft.AspNetCore.Components;
using TripFund.App.Models;
using TripFund.App.Utilities;
using TripFund.App.Services;

namespace TripFund.App.Components.Common
{
    public partial class GeneralInfoForm
    {
        [Parameter] public string Name { get; set; } = "";
        [Parameter] public EventCallback<string> NameChanged { get; set; }
        
        [Parameter] public string Slug { get; set; } = "";
        [Parameter] public EventCallback<string> SlugChanged { get; set; }

        [Parameter] public DateTime StartDate { get; set; } = DateTime.Today;
        [Parameter] public EventCallback<DateTime> StartDateChanged { get; set; }

        [Parameter] public DateTime EndDate { get; set; } = DateTime.Today.AddDays(7);
        [Parameter] public EventCallback<DateTime> EndDateChanged { get; set; }

        [Parameter] public string Description { get; set; } = "";
        [Parameter] public EventCallback<string> DescriptionChanged { get; set; }

        [Parameter] public bool IsCreate { get; set; } = false;

        private string dateError = "";

        private async Task OnNameInput(ChangeEventArgs e)
        {
            var newName = e.Value?.ToString() ?? "";
            Name = newName;
            await NameChanged.InvokeAsync(newName);

            if (IsCreate)
            {
                var newSlug = SlugUtility.GenerateSlug(newName);
                Slug = newSlug;
                await SlugChanged.InvokeAsync(newSlug);
            }
        }

        private async Task OnDescriptionInput(ChangeEventArgs e)
        {
            var val = e.Value?.ToString() ?? "";
            Description = val;
            await DescriptionChanged.InvokeAsync(val);
        }

        private async Task TrimName()
        {
            var trimmed = Name?.Trim() ?? "";
            if (Name != trimmed)
            {
                Name = trimmed;
                await NameChanged.InvokeAsync(Name);
            }
        }

        private async Task TrimDescription()
        {
            var trimmed = Description?.Trim() ?? "";
            if (Description != trimmed)
            {
                Description = trimmed;
                await DescriptionChanged.InvokeAsync(Description);
            }
        }
    }
}

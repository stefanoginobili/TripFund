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

        [Parameter] public string Suffix { get; set; } = "";

        [Parameter] public bool IsCreate { get; set; } = false;
        [Parameter] public bool IsReadonly { get; set; } = false;

        private string FullSlug => SlugUtility.GenerateSlug(Slug + (string.IsNullOrEmpty(Suffix) ? "" : "_" + Suffix));

        private string dateError = "";
        private TimeSpan _currentDuration;

        protected override void OnParametersSet()
        {
            // Normalize incoming dates to Unspecified to prevent timezone shifts during arithmetic or display
            if (StartDate.Kind != DateTimeKind.Unspecified) StartDate = DateTime.SpecifyKind(StartDate, DateTimeKind.Unspecified);
            if (EndDate.Kind != DateTimeKind.Unspecified) EndDate = DateTime.SpecifyKind(EndDate, DateTimeKind.Unspecified);
            
            // Capture the duration whenever parameters are updated from the parent
            _currentDuration = EndDate - StartDate;
        }

        private async Task OnStartDateChanged(DateTime val)
        {
            val = DateTime.SpecifyKind(val, DateTimeKind.Unspecified);
            
            // Calculate new end date based on the baseline duration captured before the change
            var newEndDate = val.Add(_currentDuration);
            
            StartDate = val;
            EndDate = newEndDate;
            
            await StartDateChanged.InvokeAsync(val);
            await EndDateChanged.InvokeAsync(newEndDate);
            StateHasChanged();
        }

        private async Task OnEndDateChanged(DateTime val)
        {
            val = DateTime.SpecifyKind(val, DateTimeKind.Unspecified);
            if (val < StartDate)
            {
                dateError = "La data di fine deve essere successiva a quella di inizio.";
                return;
            }
            dateError = "";
            EndDate = val;
            
            // Update the baseline duration as the user is manually changing it
            _currentDuration = EndDate - StartDate;
            
            await EndDateChanged.InvokeAsync(val);
        }

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

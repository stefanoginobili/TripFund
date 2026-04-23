using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Common;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TripFund.Tests.Components
{
    public class CurrenciesFormTests : BunitContext
    {
        private readonly Mock<IAlertService> _alertMock;

        public CurrenciesFormTests()
        {
            _alertMock = new Mock<IAlertService>();
            Services.AddSingleton(_alertMock.Object);
            
            // Mock JS Interop
            JSInterop.SetupVoid("appLogic.selectText", _ => true);
            JSInterop.SetupVoid("appLogic.lockScroll");
            JSInterop.SetupVoid("appLogic.unlockScroll");
            JSInterop.SetupVoid("appLogic.positionMenu", _ => true);
        }

        [Fact]
        public void ShouldShowSuggestions_WhenTypingInCodeField()
        {
            // Arrange
            var cut = Render<CurrenciesForm>(parameters => parameters
                .Add(p => p.Currencies, new Dictionary<string, Currency>())
            );

            // Ensure we are in "Add" mode
            // (It's default if Currencies is empty)

            // Act
            var input = cut.Find("input[placeholder='EUR']");
            input.Input("EU");

            // Assert
            var dropdown = cut.Find(".suggestions-dropdown");
            dropdown.Should().NotBeNull();
            
            var items = cut.FindAll(".suggestion-item");
            items.Should().NotBeEmpty();
            items.Any(i => i.TextContent.Contains("EUR")).Should().BeTrue();
        }

        [Fact]
        public void ShouldPrioritizeCodeMatches_WhenTyping()
        {
            // Arrange
            var cut = Render<CurrenciesForm>(parameters => parameters
                .Add(p => p.Currencies, new Dictionary<string, Currency>())
            );

            // Act
            var input = cut.Find("input[placeholder='EUR']");
            input.Input("US"); // Should match USD first

            // Assert
            var firstItem = cut.Find(".suggestion-item");
            firstItem.TextContent.Should().Contain("USD");
        }

        [Fact]
        public void ShouldMatchName_WhenTyping()
        {
            // Arrange
            var cut = Render<CurrenciesForm>(parameters => parameters
                .Add(p => p.Currencies, new Dictionary<string, Currency>())
            );

            // Act
            var input = cut.Find("input[placeholder='EUR']");
            input.Input("Dollar");

            // Assert
            var items = cut.FindAll(".suggestion-item");
            items.Should().Contain(i => i.TextContent.Contains("USD"));
            items.Should().Contain(i => i.TextContent.Contains("CAD"));
        }

        [Fact]
        public void ShouldPopulateFields_WhenSelectingSuggestion()
        {
            // Arrange
            var cut = Render<CurrenciesForm>(parameters => parameters
                .Add(p => p.Currencies, new Dictionary<string, Currency>())
            );

            var input = cut.Find("input[placeholder='EUR']");
            input.Input("EUR");

            // Act
            var suggestion = cut.Find(".suggestion-item");
            suggestion.MouseDown(); // We use onmousedown

            // Assert
            cut.Find("input[placeholder='EUR']").GetAttribute("value").Should().Be("EUR");
            cut.Find("input[placeholder='€']").GetAttribute("value").Should().Be("€");
            
            // Find decimals input - it doesn't have a placeholder but it's the 3rd input in the grid
            var allInputs = cut.FindAll(".new-currency-grid input");
            allInputs[2].GetAttribute("value").Should().Be("2");
            
            // Dropdown should be gone
            cut.FindAll(".suggestions-dropdown").Should().BeEmpty();
        }

        [Fact]
        public async Task ShouldHideSuggestions_OnBlurAfterDelay()
        {
            // Arrange
            var cut = Render<CurrenciesForm>(parameters => parameters
                .Add(p => p.Currencies, new Dictionary<string, Currency>())
            );

            var input = cut.Find("input[placeholder='EUR']");
            input.Input("EU");
            cut.FindAll(".suggestions-dropdown").Should().NotBeEmpty();

            // Act
            input.Blur();
            await Task.Delay(300); // Wait for the 200ms delay in OnCodeBlur

            // Assert
            cut.FindAll(".suggestions-dropdown").Should().BeEmpty();
        }
    }
}

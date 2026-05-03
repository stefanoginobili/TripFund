using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Common;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace TripFund.Tests.Components;

public class CategoriesFormTests : BunitContext
{
    private readonly Mock<IAlertService> _alertMock;

    public CategoriesFormTests()
    {
        _alertMock = new Mock<IAlertService>();
        Services.AddSingleton(_alertMock.Object);
        Services.AddSingleton<INavigationService>(sp => 
        {
            var navService = new NavigationService();
            navService.Register(sp.GetRequiredService<NavigationManager>());
            return navService;
        });

        JSInterop.SetupVoid("appLogic.positionMenu", _ => true);
        JSInterop.SetupVoid("appLogic.scrollIntoView", _ => true);
        JSInterop.SetupVoid("appLogic.initSortable", _ => true);
        JSInterop.SetupVoid("appLogic.destroySortable", _ => true);
        JSInterop.SetupVoid("appLogic.lockScroll");
        JSInterop.SetupVoid("appLogic.unlockScroll");
    }

    [Fact]
    public async Task DeleteCategory_ShouldShowConfirmation_AndRemoveOnConfirm()
    {
        // Arrange
        var categories = new Dictionary<string, ExpenseCategory>
        {
            { "food", new ExpenseCategory { Name = "Cibo", Icon = "🍕", Color = "#FF0000" } }
        };

        var categoriesChangedCalled = false;
        var cut = Render<CategoriesForm>(parameters => parameters
            .Add(p => p.Categories, categories)
            .Add(p => p.CategoriesChanged, EventCallback.Factory.Create<Dictionary<string, ExpenseCategory>>(this, (dict) => {
                categoriesChangedCalled = true;
                categories = dict;
            }))
        );

        _alertMock.Setup(a => a.ConfirmAsync(
            "Elimina Categoria",
            It.Is<string>(s => s.Contains("Cibo")),
            "Elimina",
            "Annulla",
            AlertType.Warning,
            "center"))
            .ReturnsAsync(true);

        // Act
        // Open the action menu
        await cut.Find(".action-trigger").ClickAsync();
        
        // Find and click the delete button (the one with text-danger)
        var deleteBtn = cut.FindAll(".dropdown-item-vibe.text-danger").First(b => b.TextContent.Contains("Elimina"));
        await deleteBtn.ClickAsync();

        // Assert
        _alertMock.Verify(a => a.ConfirmAsync("Elimina Categoria", It.IsAny<string>(), "Elimina", "Annulla", AlertType.Warning, "center"), Times.Once);
        categories.Should().NotContainKey("food");
        categoriesChangedCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCategory_ShouldShowConfirmation_AndNotRemoveOnCancel()
    {
        // Arrange
        var categories = new Dictionary<string, ExpenseCategory>
        {
            { "food", new ExpenseCategory { Name = "Cibo", Icon = "🍕", Color = "#FF0000" } }
        };

        var categoriesChangedCalled = false;
        var cut = Render<CategoriesForm>(parameters => parameters
            .Add(p => p.Categories, categories)
            .Add(p => p.CategoriesChanged, EventCallback.Factory.Create<Dictionary<string, ExpenseCategory>>(this, (dict) => {
                categoriesChangedCalled = true;
            }))
        );

        _alertMock.Setup(a => a.ConfirmAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<AlertType>(),
            It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        await cut.Find(".action-trigger").ClickAsync();
        var deleteBtn = cut.FindAll(".dropdown-item-vibe.text-danger").First(b => b.TextContent.Contains("Elimina"));
        await deleteBtn.ClickAsync();

        // Assert
        _alertMock.Verify(a => a.ConfirmAsync("Elimina Categoria", It.IsAny<string>(), "Elimina", "Annulla", AlertType.Warning, "center"), Times.Once);
        categories.Should().ContainKey("food");
        categoriesChangedCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ReorderCategories_ShouldUpdateOrder()
    {
        // Arrange
        var categories = new Dictionary<string, ExpenseCategory>
        {
            { "food", new ExpenseCategory { Name = "Cibo", Icon = "🍕", Color = "#FF0000" } },
            { "transport", new ExpenseCategory { Name = "Trasporto", Icon = "🚌", Color = "#0000FF" } }
        };

        var cut = Render<CategoriesForm>(parameters => parameters
            .Add(p => p.Categories, categories)
            .Add(p => p.CategoriesChanged, EventCallback.Factory.Create<Dictionary<string, ExpenseCategory>>(this, (dict) => {
                categories = dict;
            }))
        );

        // Act
        var sortableList = cut.FindComponent<SortableList<KeyValuePair<string, ExpenseCategory>>>();
        await cut.InvokeAsync(() => sortableList.Instance.OnReorder.InvokeAsync((0, 1)));

        // Assert
        var orderedKeys = categories.Keys.ToList();
        orderedKeys[0].Should().Be("transport");
        orderedKeys[1].Should().Be("food");
    }
}

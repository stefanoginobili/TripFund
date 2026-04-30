using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TripFund.App.Components.Common;
using TripFund.App.Services;
using FluentAssertions;

namespace TripFund.Tests.Components;

public class VibeAlertTests : BunitContext
{
    public VibeAlertTests()
    {
        Services.AddSingleton<INavigationService>(sp => 
        {
            var navService = new NavigationService();
            navService.Register(sp.GetRequiredService<NavigationManager>());
            return navService;
        });
    }

    [Fact]
    public void VibeAlert_ShouldNotRenderWhenNotVisible()
    {
        // Arrange
        var service = new VibeAlertService();
        Services.AddSingleton<IAlertService>(service);

        // Act
        var cut = Render<VibeAlert>();

        // Assert
        cut.Markup.Should().BeEmpty();
    }

    [Fact]
    public void VibeAlert_ShouldRenderCorrectly_WhenAlert()
    {
        // Arrange
        var service = new VibeAlertService();
        Services.AddSingleton<IAlertService>(service);
        var cut = Render<VibeAlert>();

        // Act
        _ = service.ShowAlertAsync("Alert Title", "Alert Message", "Got it");

        // Assert
        cut.Find(".alert-title-vibe").TextContent.Should().Be("Alert Title");
        cut.Find(".alert-message-vibe").TextContent.Should().Be("Alert Message");
        cut.Find("button.btn-primary-vibe").TextContent.Trim().Should().Be("Got it");
        cut.FindAll("button").Should().HaveCount(1);
    }

    [Fact]
    public void VibeAlert_ShouldRenderCorrectly_WhenConfirm()
    {
        // Arrange
        var service = new VibeAlertService();
        Services.AddSingleton<IAlertService>(service);
        var cut = Render<VibeAlert>();

        // Act
        _ = service.ConfirmAsync("Confirm Title", "Confirm Message", "Yes", "No");

        // Assert
        cut.Find(".alert-title-vibe").TextContent.Should().Be("Confirm Title");
        cut.Find(".alert-message-vibe").TextContent.Should().Be("Confirm Message");
        cut.Find("button.btn-primary-vibe").TextContent.Trim().Should().Be("Yes");
        cut.Find("button.btn-secondary-vibe").TextContent.Trim().Should().Be("No");
    }

    [Fact]
    public void VibeAlert_ShouldRenderCorrectly_WhenWarning()
    {
        // Arrange
        var service = new VibeAlertService();
        Services.AddSingleton<IAlertService>(service);
        var cut = Render<VibeAlert>();

        // Act
        _ = service.ConfirmAsync("Elimina", "Sei sicuro?", "Elimina", "Annulla", AlertType.Warning);

        // Assert
        cut.Find(".alert-modal-vibe").ClassList.Should().Contain("warning");
        cut.Find("button.btn-warning-vibe").TextContent.Trim().Should().Be("Elimina");
    }

    [Fact]
    public void VibeAlert_ShouldRenderCorrectly_WhenError()
    {
        // Arrange
        var service = new VibeAlertService();
        Services.AddSingleton<IAlertService>(service);
        var cut = Render<VibeAlert>();

        // Act
        _ = service.ShowAlertAsync("Errore", "Messaggio errore", "Chiudi", AlertType.Error);

        // Assert
        cut.Find(".alert-modal-vibe").ClassList.Should().Contain("error");
        cut.Find("button.btn-error-vibe").TextContent.Trim().Should().Be("Chiudi");
    }

    [Fact]
    public async Task VibeAlert_ClickButtons_ShouldCallServiceClose()
    {
        // Arrange
        var service = new VibeAlertService();
        Services.AddSingleton<IAlertService>(service);
        var cut = Render<VibeAlert>();
        var task = service.ConfirmAsync("Title", "Message", "OK", "Cancel");

        // Act
        cut.Find("button.btn-primary-vibe").Click();

        // Assert
        var result = await task;
        result.Should().BeTrue();
        service.IsVisible.Should().BeFalse();
    }
}

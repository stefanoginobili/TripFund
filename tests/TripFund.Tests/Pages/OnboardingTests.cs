using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace TripFund.Tests.Pages;

public class OnboardingTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;

    public OnboardingTests()
    {
        var itCulture = new System.Globalization.CultureInfo("it-IT");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = itCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = itCulture;

        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        Services.AddSingleton(_storageMock.Object);
    }

    [Fact]
    public void Onboarding_ShouldRenderCorrectly()
    {
        // Act
        var cut = Render<Onboarding>();

        // Assert
        cut.Find("h1").TextContent.Should().Be("TripFund");
        cut.Find("h2").TextContent.Should().Be("Benvenuto in TripFund");
    }

    [Fact]
    public async Task Onboarding_Save_ShouldStoreSettingsAndNavigate()
    {
        // Arrange
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<Onboarding>();
        var input = cut.Find("#authorName");

        // Act
        input.Input("Mario Rossi");
        cut.Find("button").Click();

        // Assert
        _storageMock.Verify(s => s.SaveAppSettingsAsync(It.Is<AppSettings>(a => a.AuthorName == "Mario Rossi" && a.DeviceId.StartsWith("mario-rossi"))), Times.Once);
        nav.Uri.Should().EndWith("/");
    }
}

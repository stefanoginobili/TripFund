using TripFund.App.Services;
using FluentAssertions;

namespace TripFund.Tests.Services;

public class VibeAlertServiceTests
{
    [Fact]
    public void VibeAlertService_ShowAlert_ShouldUpdateStateAndNotify()
    {
        // Arrange
        var service = new VibeAlertService();
        var notifyCalled = false;
        service.OnChange += () => notifyCalled = true;

        // Act
        var task = service.ShowAlertAsync("Title", "Message", "Cancel", AlertType.Error);

        // Assert
        service.IsVisible.Should().BeTrue();
        service.Title.Should().Be("Title");
        service.Message.Should().Be("Message");
        service.AcceptText.Should().Be("Cancel");
        service.IsConfirm.Should().BeFalse();
        service.Type.Should().Be(AlertType.Error);
        notifyCalled.Should().BeTrue();
        task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task VibeAlertService_Close_ShouldCompleteTask()
    {
        // Arrange
        var service = new VibeAlertService();
        var task = service.ConfirmAsync("Title", "Message", "Accept", "Cancel", AlertType.Warning);

        // Act
        service.Close(true);

        // Assert
        service.IsVisible.Should().BeFalse();
        service.Type.Should().Be(AlertType.Warning);
        var result = await task;
        result.Should().BeTrue();
    }

    [Fact]
    public async Task VibeAlertService_Confirm_ShouldReturnResult()
    {
        // Arrange
        var service = new VibeAlertService();
        var task = service.ConfirmAsync("Title", "Message", "OK", "No");

        // Act
        service.Close(false);

        // Assert
        var result = await task;
        result.Should().BeFalse();
    }
}

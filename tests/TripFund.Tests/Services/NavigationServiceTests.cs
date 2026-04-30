using Microsoft.AspNetCore.Components;
using Moq;
using TripFund.App.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using System.Threading.Tasks;

namespace TripFund.Tests.Services
{
    public class NavigationServiceTests : BunitContext
    {
        private readonly NavigationManager _nav;
        private readonly NavigationService _service;

        public NavigationServiceTests()
        {
            Services.AddSingleton<INavigationService>(sp => 
            {
                var navService = new NavigationService();
                navService.Register(sp.GetRequiredService<NavigationManager>());
                return navService;
            });
            _service = (NavigationService)Services.GetRequiredService<INavigationService>();
            _nav = Services.GetRequiredService<NavigationManager>();
        }

        [Fact]
        public async Task NavigateAsync_ShouldPushToStackAndNavigate()
        {
            // Arrange
            string from = "/page1";
            string to = "http://localhost/page2";

            // Act
            await _service.NavigateAsync(from, to);

            // Assert
            Assert.Equal(1, _service.StackCount);
            _nav.Uri.Should().Be(to);
        }

        [Fact]
        public async Task NavigateAsync_ShouldNotPushDuplicate()
        {
            // Arrange
            await _service.NavigateAsync("/", "/page1"); // Stack: ["/"]
            
            // Act
            await _service.NavigateAsync("/page1", "/page1"); // fromUrl == toUrl
            
            // Assert
            Assert.Equal(1, _service.StackCount);
            _nav.Uri.Should().Be("http://localhost/page1");
        }

        [Fact]
        public async Task NavigateAsync_ShouldRespectBeforeNavigateAction_Veto()
        {
            // Arrange
            _service.SetBeforeNavigateAction(() => Task.FromResult(false)); // Veto!
            var currentUri = _nav.Uri;

            // Act
            await _service.NavigateAsync("/from", "http://localhost/to");

            // Assert
            Assert.Equal(0, _service.StackCount);
            _nav.Uri.Should().Be(currentUri);
        }

        [Fact]
        public async Task GoBackAsync_ShouldPopAndNavigate_WhenStackHasItems()
        {
            // Arrange
            await _service.NavigateAsync("/page1", "http://localhost/page2");
            await _service.NavigateAsync("/page2", "http://localhost/page3");
            
            // Act
            var result = await _service.GoBackAsync();

            // Assert
            Assert.True(result);
            Assert.Equal(1, _service.StackCount);
            _nav.Uri.Should().Be("http://localhost/page2");
        }

        [Fact]
        public async Task GoBackAsync_ShouldReturnFalse_WhenStackIsEmpty()
        {
            // Act
            var initialUri = _nav.Uri;
            var result = await _service.GoBackAsync();

            // Assert
            Assert.False(result);
            _nav.Uri.Should().Be(initialUri);
        }

        [Fact]
        public async Task GoBackAsync_ShouldRespectBeforeNavigateAction_Veto()
        {
            // Arrange
            await _service.NavigateAsync("/page1", "http://localhost/page2");
            _service.SetBeforeNavigateAction(() => Task.FromResult(false)); // Veto!
            var currentUri = _nav.Uri;

            // Act
            var result = await _service.GoBackAsync();

            // Assert
            Assert.True(result); // Handled by staying
            Assert.Equal(1, _service.StackCount); // Nothing popped
            _nav.Uri.Should().Be(currentUri);
        }

        [Fact]
        public async Task GoBackAsync_ShouldRespectBeforeNavigateAction_Proceed()
        {
            // Arrange
            await _service.NavigateAsync("http://localhost/page1", "http://localhost/page2");
            _service.SetBeforeNavigateAction(() => Task.FromResult(true)); // Proceed

            // Act
            var result = await _service.GoBackAsync();

            // Assert
            Assert.True(result);
            Assert.Equal(0, _service.StackCount); // Popped
            _nav.Uri.Should().Be("http://localhost/page1");
        }
    }
}

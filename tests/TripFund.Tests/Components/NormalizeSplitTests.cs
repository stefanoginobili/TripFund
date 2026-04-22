using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Components.Common;
using FluentAssertions;
using Xunit;
using System.Reflection;

namespace TripFund.Tests.Components;

public class NormalizeSplitTests
{
    private class TestConflictResolverModal : ConflictResolverModal
    {
        public string CallNormalizeSplit(Transaction? t, TripConfig? config)
        {
            var field = typeof(ConflictResolverModal).GetField("currentConfig", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(this, config);
            
            var method = typeof(ConflictResolverModal).GetMethod("NormalizeSplit", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new Exception("Method NormalizeSplit not found");
            var result = method.Invoke(this, new object?[] { t });
            return (string)result!;
        }
    }

    [Fact]
    public void NormalizeSplit_ShouldFormatCorrectly()
    {
        // Arrange
        var modal = new TestConflictResolverModal();
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Decimals = 2 } },
                { "JPY", new Currency { Decimals = 0 } },
                { "BHD", new Currency { Decimals = 3 } }
            }
        };

        var t = new Transaction
        {
            Currency = "EUR",
            Split = new Dictionary<string, SplitInfo>
            {
                { "user1", new SplitInfo { Amount = 10.5m, Manual = false } },
                { "user2", new SplitInfo { Amount = 20.0m, Manual = true } }
            }
        };

        // Act
        var result = modal.CallNormalizeSplit(t, config);

        // Assert
        // Result should use it-IT culture (comma as decimal separator)
        // Order by key: user1, user2
        result.Should().Be("user1: 10,50 (auto)<br /><br />user2: 20,00 (manuale)");
    }

    [Fact]
    public void NormalizeSplit_ShouldUseDifferentDecimals()
    {
        // Arrange
        var modal = new TestConflictResolverModal();
        var config = new TripConfig
        {
            Currencies = new Dictionary<string, Currency>
            {
                { "JPY", new Currency { Decimals = 0 } },
                { "BHD", new Currency { Decimals = 3 } }
            }
        };

        var tJPY = new Transaction
        {
            Currency = "JPY",
            Split = new Dictionary<string, SplitInfo>
            {
                { "u1", new SplitInfo { Amount = 1500.75m, Manual = false } }
            }
        };

        var tBHD = new Transaction
        {
            Currency = "BHD",
            Split = new Dictionary<string, SplitInfo>
            {
                { "u1", new SplitInfo { Amount = 1.2345m, Manual = true } }
            }
        };

        // Act & Assert
        modal.CallNormalizeSplit(tJPY, config).Should().Be("u1: 1.501 (auto)"); // it-IT formats 1500.75 as 1.501 with N0? Wait, N0 should be 1.501? 
        // Actually N0 for 1500.75 is 1.501 in it-IT because it rounds.
        
        // Let's re-verify it-IT N0 for 1500.75. 
        // 1500,75 rounded to 0 decimals is 1501. 
        // In it-IT it is "1.501" (dot as thousands separator).

        modal.CallNormalizeSplit(tBHD, config).Should().Be("u1: 1,235 (manuale)");
    }

    [Fact]
    public void NormalizeSplit_ShouldHandleMissingConfigOrCurrency()
    {
        // Arrange
        var modal = new TestConflictResolverModal();
        
        var t = new Transaction
        {
            Currency = "USD",
            Split = new Dictionary<string, SplitInfo>
            {
                { "u1", new SplitInfo { Amount = 10m, Manual = false } }
            }
        };

        // Act & Assert
        // Should default to 2 decimals
        modal.CallNormalizeSplit(t, null).Should().Be("u1: 10,00 (auto)");
        modal.CallNormalizeSplit(t, new TripConfig()).Should().Be("u1: 10,00 (auto)");
    }
}

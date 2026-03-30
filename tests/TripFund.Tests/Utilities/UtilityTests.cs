using FluentAssertions;
using TripFund.App.Utilities;

namespace TripFund.Tests.Utilities;

public class UtilityTests
{
    [Theory]
    [InlineData("Mario Rossi", "mario-rossi")]
    [InlineData("Patagonia 2026!", "patagonia-2026")]
    [InlineData("  Space   Test  ", "space-test")]
    [InlineData("Upper Case", "upper-case")]
    [InlineData("hyphen-ated", "hyphen-ated")]
    public void SlugUtility_GenerateSlug_ShouldReturnCorrectSlug(string input, string expected)
    {
        // Act
        var result = SlugUtility.GenerateSlug(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void TransactionIdGenerator_GenerateId_ShouldReturnCorrectFormat()
    {
        // Act
        var id = TransactionIdGenerator.GenerateId();

        // Assert
        // Format: yyyyMMddTHHmmssZ-[guid-prefix] (8 chars)
        id.Should().MatchRegex(@"^\d{8}T\d{6}Z-[a-f0-9]{8}$");
    }

    [Fact]
    public void TransactionIdGenerator_GenerateId_ShouldBeUnique()
    {
        // Act
        var id1 = TransactionIdGenerator.GenerateId();
        var id2 = TransactionIdGenerator.GenerateId();

        // Assert
        id1.Should().NotBe(id2);
    }
}

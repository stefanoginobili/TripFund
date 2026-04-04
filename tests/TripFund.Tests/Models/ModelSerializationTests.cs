using System.Text.Json;
using FluentAssertions;
using TripFund.App.Models;

namespace TripFund.Tests.Models;

public class ModelSerializationTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void TripConfig_ShouldSerializeAndDeserializeCorrectly()
    {
        // Arrange
        var json = @"{
          ""id"": ""guid-trip-1234"",
          ""name"": ""Patagonia 2026"",
          ""description"": ""Roadtrip in Argentina and Chile"",
          ""startDate"": ""2026-11-01T00:00:00Z"",
          ""endDate"": ""2026-11-20T00:00:00Z"",
          ""createdAt"": ""2026-03-24T12:00:00Z"",
          ""updatedAt"": ""2026-03-27T13:40:00Z"",
          ""author"": ""Mario Rossi"",
          ""currencies"": {
            ""EUR"": { ""symbol"": ""€"", ""name"": ""Euro"", ""expectedQuotaPerMember"": 500.00 },
            ""ARS"": { ""symbol"": ""$"", ""name"": ""Argentine Peso"", ""expectedQuotaPerMember"": 150000.00 }
          },
          ""members"": {
            ""mario-rossi"": { ""name"": ""Mario Rossi"", ""email"": ""mario@example.com"", ""avatar"": ""🎒"" },
            ""luigi"": { ""name"": ""Luigi"", ""email"": ""luigi@example.com"", ""avatar"": ""👤"" }
          }
        }";

        // Act
        var tripConfig = JsonSerializer.Deserialize<TripConfig>(json, _options);
        var serializedJson = JsonSerializer.Serialize(tripConfig, _options);
        var deserializedAgain = JsonSerializer.Deserialize<TripConfig>(serializedJson, _options);

        // Assert
        tripConfig.Should().NotBeNull();
        tripConfig!.Id.Should().Be("guid-trip-1234");
        tripConfig.Name.Should().Be("Patagonia 2026");
        tripConfig.Author.Should().Be("Mario Rossi");
        tripConfig.UpdatedAt.Should().Be(DateTime.Parse("2026-03-27T13:40:00Z").ToUniversalTime());
        tripConfig.Currencies.Should().HaveCount(2);
        tripConfig.Currencies["EUR"].ExpectedQuotaPerMember.Should().Be(500.00m);
        tripConfig.Members.Should().HaveCount(2);
        tripConfig.Members["mario-rossi"].Name.Should().Be("Mario Rossi");

        deserializedAgain.Should().BeEquivalentTo(tripConfig);
    }

    [Fact]
    public void Transaction_ShouldSerializeAndDeserializeCorrectly()
    {
        // Arrange
        var json = @"{
          ""id"": ""20260325T143000Z-a1b2c3d4"",
          ""type"": ""expense"", 
          ""date"": ""2026-03-25T14:30:00Z"",
          ""createdAt"": ""2026-03-25T14:30:00Z"",
          ""updatedAt"": ""2026-03-26T11:20:00Z"",
          ""currency"": ""ARS"",
          ""amount"": 15000.50,
          ""description"": ""Cena a Buenos Aires"",
          ""author"": ""Mario Rossi"",
          ""split"": {
            ""mario-rossi"": 10000.00,
            ""luigi"": 5000.50
          }, 
          ""location"": {
            ""latitude"": -34.6037,
            ""longitude"": -58.3816,
            ""name"": ""Restaurante El Gaucho""
          },
          ""attachments"": [
            ""attachment_abc123.jpg""
          ]
        }";

        // Act
        var transaction = JsonSerializer.Deserialize<Transaction>(json, _options);
        var serializedJson = JsonSerializer.Serialize(transaction, _options);
        var deserializedAgain = JsonSerializer.Deserialize<Transaction>(serializedJson, _options);

        // Assert
        transaction.Should().NotBeNull();
        transaction!.Id.Should().Be("20260325T143000Z-a1b2c3d4");
        transaction.CreatedAt.Should().Be(DateTime.Parse("2026-03-25T14:30:00Z").ToUniversalTime());
        transaction.UpdatedAt.Should().Be(DateTime.Parse("2026-03-26T11:20:00Z").ToUniversalTime());
        transaction.Amount.Should().Be(15000.50m);
        transaction.Author.Should().Be("Mario Rossi");
        transaction.Split.Should().HaveCount(2);
        transaction.Location.Should().NotBeNull();
        transaction.Location!.Name.Should().Be("Restaurante El Gaucho");
        transaction.Attachments.Should().ContainSingle().Which.Should().Be("attachment_abc123.jpg");

        deserializedAgain.Should().BeEquivalentTo(transaction);
    }

    [Fact]
    public void AppSettings_ShouldSerializeAndDeserializeCorrectly()
    {
        // Arrange
        var json = @"{
          ""authorName"": ""Mario Rossi"",
          ""deviceId"": ""mario-rossi""
        }";

        // Act
        var settings = JsonSerializer.Deserialize<AppSettings>(json, _options);
        var serializedJson = JsonSerializer.Serialize(settings, _options);
        var deserializedAgain = JsonSerializer.Deserialize<AppSettings>(serializedJson, _options);

        // Assert
        settings.Should().NotBeNull();
        settings!.AuthorName.Should().Be("Mario Rossi");
        settings.DeviceId.Should().Be("mario-rossi");

        deserializedAgain.Should().BeEquivalentTo(settings);
    }

    [Fact]
    public void LocalTripRegistry_ShouldSerializeAndDeserializeCorrectly()
    {
        // Arrange
        var json = @"{
          ""trips"": {
            ""patagonia-2026"": {
              ""driveFolderId"": ""drive-folder-xyz""
            },
            ""giappone-2027"": {
              ""driveFolderId"": ""drive-folder-abc""
            }
          }
        }";

        // Act
        var registry = JsonSerializer.Deserialize<LocalTripRegistry>(json, _options);
        var serializedJson = JsonSerializer.Serialize(registry, _options);
        var deserializedAgain = JsonSerializer.Deserialize<LocalTripRegistry>(serializedJson, _options);

        // Assert
        registry.Should().NotBeNull();
        registry!.Trips.Should().HaveCount(2);
        registry.Trips.Should().ContainKey("patagonia-2026");
        registry.Trips["patagonia-2026"].DriveFolderId.Should().Be("drive-folder-xyz");

        deserializedAgain.Should().BeEquivalentTo(registry);
    }
}

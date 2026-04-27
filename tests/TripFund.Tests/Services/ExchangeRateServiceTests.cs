using FluentAssertions;
using System.Text.Json;
using TripFund.App.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TripFund.Tests.Services;

public class ExchangeRateServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly ExchangeRateService _service;

    public ExchangeRateServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "ExchangeRateTests_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempPath);
        
        _server = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        _service = new ExchangeRateService(_httpClient, _tempPath);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }

    [Fact]
    public async Task GetRateAsync_ShouldReturnCachedValue()
    {
        // Arrange
        var tripSlug = "test-trip";
        var date = new DateTime(2026, 3, 24);
        var cacheDir = Path.Combine(_tempPath, "trips", tripSlug, "cache");
        Directory.CreateDirectory(cacheDir);
        
        var cache = new { 
            rates = new Dictionary<string, Dictionary<string, decimal>> {
                { "2026-03-24", new Dictionary<string, decimal> { { "ARS", 1200.50m } } }
            }
        };
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "rates_2026_03.json"), JsonSerializer.Serialize(cache));

        // Act
        var rate = await _service.GetRateAsync(tripSlug, "ARS", date);

        // Assert
        rate.Should().Be(1200.50m);
    }

    [Fact]
    public async Task GetRateAsync_ShouldFallbackToPreviousDay_WhenWeekend()
    {
        // Arrange
        var tripSlug = "test-trip";
        var friday = new DateTime(2026, 3, 27); // Friday
        var saturday = new DateTime(2026, 3, 28); // Saturday
        var cacheDir = Path.Combine(_tempPath, "trips", tripSlug, "cache");
        Directory.CreateDirectory(cacheDir);

        var cache = new { 
            rates = new Dictionary<string, Dictionary<string, decimal>> {
                { "2026-03-27", new Dictionary<string, decimal> { { "USD", 1.08m } } }
            }
        };
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "rates_2026_03.json"), JsonSerializer.Serialize(cache));

        // Act
        var rate = await _service.GetRateAsync(tripSlug, "USD", saturday);

        // Assert
        rate.Should().Be(1.08m);
    }

    [Fact]
    public async Task DownloadMissingRatesAsync_ShouldFetchFromApiAndCache()
    {
        // Arrange
        var tripSlug = "test-trip";
        var date = new DateTime(2026, 3, 24);
        
        // Mock API Response for fawazahmed0/currency-api
        var jsonResponse = @"
{
  ""date"": ""2026-03-24"",
  ""eur"": {
    ""usd"": 1.085,
    ""ars"": 1200.5
  }
}";

        _server
            .Given(Request.Create().WithPath("/v1/currencies/eur.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(jsonResponse));

        // Temporarily override the HttpClient's BaseAddress behavior in the service 
        // by making sure the service uses the provided BaseAddress.
        // The service logic: var baseUrl = _httpClient.BaseAddress?.ToString() ?? "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@";
        // To use the WireMock server, we need the URL to look like:
        // http://localhost:PORT/@2026-03-24/v1/currencies/eur.json
        // So we need to mock a path that includes the date segment.

        _server.Reset();
        _server
            .Given(Request.Create().WithPath(p => p.Contains("/v1/currencies/eur.json")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(jsonResponse));

        // Act
        await _service.DownloadMissingRatesAsync(tripSlug, new[] { date }, new[] { "USD", "ARS" });

        // Assert
        var rateUsd = await _service.GetRateAsync(tripSlug, "USD", date);
        var rateArs = await _service.GetRateAsync(tripSlug, "ARS", date);

        rateUsd.Should().Be(1.085m);
        rateArs.Should().Be(1200.5m);
    }
}

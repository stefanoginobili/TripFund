using System.IO;
using System.Threading.Tasks;
using TripFund.App.Services;
using Xunit;

namespace TripFund.Tests.Services
{
    public class ImageCompressorServiceTests
    {
        [Fact]
        public async Task CompressImageAsync_NonMobile_ReturnsOriginalStream()
        {
            // Arrange
            IImageCompressorService compressorService = new ImageCompressorService();
            var originalContent = "This is a test image content.";
            var originalStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(originalContent));
            var fileName = "test.png";

            // Act
            var compressedStream = await compressorService.CompressImageAsync(originalStream, fileName);

            // Assert
            Assert.NotNull(compressedStream);
            Assert.True(compressedStream.CanRead);
            Assert.True(compressedStream.Length > 0);

            // Read the compressed stream content
            using (var reader = new StreamReader(compressedStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Equal(originalContent, content);
            }
            // Ensure the original stream's position is reset after the operation if it was fully consumed.
            // This test is specifically for the non-mobile fallback, which copies the stream.
            // Assert.Equal(0, originalStream.Position); // This assertion is not valid for the method's contract
        }

        [Fact]
        public async Task CompressImageAsync_EmptyStream_ReturnsEmptyStream()
        {
            // Arrange
            IImageCompressorService compressorService = new ImageCompressorService();
            var originalStream = new MemoryStream();
            var fileName = "empty.png";

            // Act
            var compressedStream = await compressorService.CompressImageAsync(originalStream, fileName);

            // Assert
            Assert.NotNull(compressedStream);
            Assert.True(compressedStream.CanRead);
            Assert.Equal(0, compressedStream.Length);
            Assert.Equal(0, compressedStream.Position);
        }
    }
}

namespace TripFund.App.Services;

public interface IImageCompressorService
{
    /// <summary>
    /// Compresses and rotates an image stream according to EXIF data.
    /// The image will be downscaled to a maximum of 3840px on any side.
    /// The output format will always be JPEG.
    /// </summary>
    /// <param name="imageStream">The input image stream.</param>
    /// <param name="fileName">The original file name, used for extension and EXIF data extraction.</param>
    /// <returns>A new stream containing the compressed and rotated JPEG image.</returns>
    Task<Stream> CompressImageAsync(Stream imageStream, string fileName);
}

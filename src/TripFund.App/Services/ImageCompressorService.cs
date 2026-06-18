using System.IO;
using Microsoft.Maui.Graphics;
#if IOS
using UIKit;
using Foundation;
using CoreGraphics;
#elif ANDROID
using Android.Graphics;
using Android.Media;
using Android.Content;
#endif

namespace TripFund.App.Services;

public class ImageCompressorService : IImageCompressorService
{
    private const int MaxImageDimension = 3840;
    private const int JpegQuality = 85; // 0-100

    public async Task<System.IO.Stream> CompressImageAsync(System.IO.Stream imageStream, string fileName)
    {
        // Ensure we can seek and read from the beginning
        if (!imageStream.CanSeek)
        {
            var ms = new System.IO.MemoryStream();
            await imageStream.CopyToAsync(ms);
            ms.Position = 0;
            imageStream = ms;
        }
        else
        {
            imageStream.Position = 0;
        }

        System.IO.MemoryStream outputStream = new System.IO.MemoryStream();

#if IOS
        await CompressImageIOS(imageStream, outputStream);
#elif ANDROID
        // For Android, we need to save to a temporary file first to use ExifInterface
        // It's a bit more involved due to how Android's ExifInterface works directly with file paths.
        string tempFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
        try
        {
            using (var fileStream = File.Create(tempFilePath))
            {
                await imageStream.CopyToAsync(fileStream);
            }
            await CompressImageAndroid(tempFilePath, outputStream);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
#else
        // Fallback for other platforms: return original stream (or a copy if not seekable)
        await imageStream.CopyToAsync(outputStream);
#endif

        outputStream.Position = 0;
        return outputStream;
    }

#if IOS
    private async Task CompressImageIOS(Stream imageStream, Stream outputStream)
    {
        var data = NSData.FromStream(imageStream);
        var originalImage = UIImage.FromData(data);

        if (originalImage == null)
        {
            // Fallback: copy original stream if UIImage creation fails
            imageStream.Position = 0;
            await imageStream.CopyToAsync(outputStream);
            return;
        }

        nfloat width = originalImage.Size.Width;
        nfloat height = originalImage.Size.Height;
        nfloat scale = 1.0f;

        if (width > MaxImageDimension || height > MaxImageDimension)
        {
            if (width > height)
            {
                scale = MaxImageDimension / width;
            }
            else
            {
                scale = MaxImageDimension / height;
            }
        }

        CGSize newSize = new CGSize(width * scale, height * scale);

        UIGraphics.BeginImageContext(newSize);
        originalImage.Draw(new CGRect(CGPoint.Empty, newSize));
        var scaledImage = UIGraphics.GetImageFromCurrentImageContext();
        UIGraphics.EndImageContext();

        if (scaledImage == null)
        {
            // Fallback: copy original stream if scaledImage creation fails
            imageStream.Position = 0;
            await imageStream.CopyToAsync(outputStream);
            return;
        }

        // iOS UIImage handles EXIF orientation automatically when drawn
        using (var jpegData = scaledImage.AsJPEG((nfloat)JpegQuality / 100f))
        {
            if (jpegData == null)
            {
                // Fallback
                imageStream.Position = 0;
                await imageStream.CopyToAsync(outputStream);
                return;
            }
            await jpegData.AsStream().CopyToAsync(outputStream);
        }
    }
#endif

#if ANDROID
    private async Task CompressImageAndroid(string filePath, System.IO.Stream outputStream)
    {
        Bitmap? bitmap = null;
        try
        {
            // First, decode bounds to get original dimensions
            BitmapFactory.Options boundsOptions = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(filePath, boundsOptions);
            int originalWidth = boundsOptions.OutWidth;
            int originalHeight = boundsOptions.OutHeight;

            int targetWidth = originalWidth;
            int targetHeight = originalHeight;

            // Calculate target dimensions based on MaxImageDimension
            if (originalWidth > MaxImageDimension || originalHeight > MaxImageDimension)
            {
                double ratio = (double)originalWidth / originalHeight;
                if (originalWidth > originalHeight) // Landscape
                {
                    targetWidth = MaxImageDimension;
                    targetHeight = (int)Math.Round(MaxImageDimension / ratio);
                }
                else // Portrait or Square
                {
                    targetHeight = MaxImageDimension;
                    targetWidth = (int)Math.Round(MaxImageDimension * ratio);
                }
            }

            // Decode the full image
            BitmapFactory.Options decodeOptions = new BitmapFactory.Options { InSampleSize = 1 };
            bitmap = BitmapFactory.DecodeFile(filePath, decodeOptions);

            if (bitmap == null)
            {
                using (var fileStream = System.IO.File.OpenRead(filePath)) { await fileStream.CopyToAsync(outputStream); }
                return;
            }

            // Create a scaled bitmap if necessary
            if (targetWidth != originalWidth || targetHeight != originalHeight)
            {
                Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, targetWidth, targetHeight, true);
                bitmap.Recycle(); // Recycle original bitmap
                bitmap = scaledBitmap;
            }

            // Handle EXIF orientation
            Matrix matrix = new Matrix();
            try
            {
                ExifInterface exif = new ExifInterface(filePath);
                int orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal);
                int rotation = 0;
                switch (orientation)
                {
                    case (int)Orientation.Rotate90: rotation = 90; break;
                    case (int)Orientation.Rotate180: rotation = 180; break;
                    case (int)Orientation.Rotate270: rotation = 270; break;
                }

                if (rotation != 0)
                {
                    matrix.PostRotate(rotation);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXIF error: {ex.Message}");
                // Ignore EXIF errors
            }

            Bitmap? rotatedBitmap = null;
            if (!matrix.IsIdentity)
            {
                rotatedBitmap = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
                bitmap.Recycle(); // Recycle original bitmap as it's no longer needed
                bitmap = rotatedBitmap;
            }

            if (bitmap == null)
            {
                // Fallback
                using (var fileStream = System.IO.File.OpenRead(filePath))
                {
                    await fileStream.CopyToAsync(outputStream);
                }
                return;
            }

            await bitmap.CompressAsync(Bitmap.CompressFormat.Jpeg!, JpegQuality, outputStream);
        }
        finally
        {
            bitmap?.Recycle();
        }
    }
#endif
}

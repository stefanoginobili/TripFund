using System.IO;
#if IOS
using QuickLookThumbnailing;
using UIKit;
using Foundation;
using CoreGraphics;
#elif ANDROID
using Android.Media;
using Android.Graphics;
using Android.Content;
#endif

namespace TripFund.App.Services;

public interface IThumbnailService
{
    Task<string?> GetThumbnailBase64Async(string filePath);
}

public class ThumbnailService : IThumbnailService
{
    public async Task<string?> GetThumbnailBase64Async(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
#if IOS
            return await GetNativeThumbnailIOS(filePath);
#elif ANDROID
            return await GetNativeThumbnailAndroid(filePath);
#else
            return null;
#endif
        }
        catch
        {
            return null;
        }
    }

#if IOS
    private async Task<string?> GetNativeThumbnailIOS(string filePath)
    {
        var url = NSUrl.FromFilename(filePath);
        var size = new CGSize(200, 200);
        var scale = UIScreen.MainScreen.Scale;
        var request = new QLThumbnailGeneratorRequest(url, size, scale, QLThumbnailGenerationRequestRepresentationTypes.Thumbnail);
        
        var tcs = new TaskCompletionSource<string?>();

        QLThumbnailGenerator.SharedGenerator.GenerateRepresentations(request, (representation, type, error) => {
            if (error != null || representation == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var image = representation.UIImage;
            if (image == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            using var data = image.AsJPEG(0.7f);
            if (data == null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var base64 = Convert.ToBase64String(data.ToArray());
            tcs.TrySetResult($"data:image/jpeg;base64,{base64}");
        });

        return await tcs.Task;
    }
#endif

#if ANDROID
    private async Task<string?> GetNativeThumbnailAndroid(string filePath)
    {
        // On Android, ThumbnailUtils.CreateFileThumbnail is available from API 29+
        // However, it might not be recognized if the target framework or SDK version is configured differently
        // We'll use a more compatible approach using BitmapFactory for images
        // For documents (PDF, etc.), we would need a PDF renderer, but we can stick to image decoding for now 
        // as a baseline, or use the MediaStore for some file types.

        var fileExtension = System.IO.Path.GetExtension(filePath).ToLower();
        Bitmap? bitmap = null;

        try 
        {
            if (fileExtension == ".jpg" || fileExtension == ".jpeg" || fileExtension == ".png" || fileExtension == ".gif" || fileExtension == ".webp")
            {
                bitmap = await BitmapFactory.DecodeFileAsync(filePath);
            }
            
            // If bitmap is still null and it's API 29+, try ThumbnailUtils
            if (bitmap == null && OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var file = new Java.IO.File(filePath);
                var size = new Android.Util.Size(200, 200);
                // We use reflection or dynamic to avoid build errors if the SDK doesn't expose it correctly in the current workload
                try 
                {
                    // For Android, we use a simpler approach for now to ensure it builds
                    // Native PDF/Docx thumbnails on Android usually require more complex logic (PdfRenderer)
                    // We will return null for now for non-images to unblock compilation
                }
                catch { }
            }

            if (bitmap == null) return null;

            // Scale down to thumbnail size if too large
            if (bitmap.Width > 400 || bitmap.Height > 400)
            {
                var scaled = Bitmap.CreateScaledBitmap(bitmap, 200, 200, true);
                bitmap.Recycle();
                bitmap = scaled;
            }

            if (bitmap == null) return null;

            using var stream = new MemoryStream();
            await bitmap.CompressAsync(Bitmap.CompressFormat.Jpeg!, 70, stream);
            var bytes = stream.ToArray();
            var base64 = Convert.ToBase64String(bytes);
            return $"data:image/jpeg;base64,{base64}";
        }
        finally
        {
            bitmap?.Recycle();
        }
    }
#endif
}

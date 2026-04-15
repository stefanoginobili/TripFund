using PdfSharp.Fonts;
using System.Reflection;

namespace TripFund.App.Utilities;

public class PdfFontResolver : IFontResolver
{
    private static readonly Dictionary<string, byte[]> FontCache = new();

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        string name = familyName.ToLower().Replace(" ", "");
        
        if (name == "roboto" || name == "sans-serif" || name == "verdana")
        {
            if (isBold) return new FontResolverInfo("Roboto-Bold");
            return new FontResolverInfo("Roboto-Regular");
        }
        
        if (name == "robotomono" || name == "monospace" || name == "couriernew")
        {
            return new FontResolverInfo("RobotoMono-Regular");
        }

        return new FontResolverInfo("Roboto-Regular");
    }

    public byte[]? GetFont(string faceName)
    {
        if (FontCache.TryGetValue(faceName, out var bytes))
        {
            return bytes;
        }

        try
        {
            var assembly = typeof(PdfFontResolver).Assembly;
            var resourceName = $"TripFund.App.Resources.Raw.Fonts.{faceName}.ttf";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                bytes = ms.ToArray();
                FontCache[faceName] = bytes;
                return bytes;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PdfFontResolver.GetFont error for {faceName}: {ex}");
        }
        
        // Return Roboto-Regular if anything else fails
        if (FontCache.TryGetValue("Roboto-Regular", out var fallback))
        {
            return fallback;
        }

        // Last-ditch effort: try to load Roboto-Regular directly
        try
        {
            var assembly = typeof(PdfFontResolver).Assembly;
            using var stream = assembly.GetManifestResourceStream("TripFund.App.Resources.Raw.Fonts.Roboto-Regular.ttf");
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                bytes = ms.ToArray();
                FontCache["Roboto-Regular"] = bytes;
                return bytes;
            }
        }
        catch { }

        return null;
    }

    public static void Register()
    {
        if (GlobalFontSettings.FontResolver is not PdfFontResolver)
        {
            GlobalFontSettings.FontResolver = new PdfFontResolver();
        }
    }

    public static Task InitializeAsync() => Task.CompletedTask;
}

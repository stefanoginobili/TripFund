using System.Text.RegularExpressions;
using System.Text;
using System.Globalization;

namespace TripFund.App.Utilities;

public static class SlugUtility
{
    public static string GenerateSlug(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return string.Empty;
        }

        string str = phrase.ToLowerInvariant();

        // Remove invalid characters
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");

        // Convert multiple spaces into one space
        str = Regex.Replace(str, @"\s+", " ").Trim();

        // Replace spaces with hyphens
        str = str.Replace(" ", "-");

        return str;
    }
}

using System.Text.RegularExpressions;
namespace SengokuProvider.Library.Services.Common;
public static class StartggNormalization
{
    public static string Normalize(string input, bool allowSuffix = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var value = input.Trim();

        if (value.StartsWith("start.gg/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("www.start.gg/", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is not ("http" or "https") || uri.Host is not ("start.gg" or "www.start.gg") || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
                throw new ArgumentException("Expected a start.gg event URL.");
            value = uri.AbsolutePath;
        }

        value = value.Trim('/');

        if (Regex.IsMatch(value, @"\Atournament/[a-zA-Z0-9_-]+/event/[a-zA-Z0-9_-]+\z")) return value;
        if (allowSuffix && Regex.IsMatch(value, @"\A[a-zA-Z0-9_-]+\z")) return value;

        throw new ArgumentException("Use a game event URL ending in /event/<slug>, not a parent tournament URL.");
    }
}

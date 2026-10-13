namespace SengokuProvider.Library.Models.Feeds;

/// <summary>Public playback links for a future channel guide; does not assert live status or account ownership.</summary>
public sealed record BroadcastLink(string Provider, string Url)
{
    public static BroadcastLink? Parse(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return null;
        if ((uri.Host is "twitch.tv" or "www.twitch.tv") && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/[a-zA-Z0-9_]{1,25}/?$"))
            return new("Twitch", uri.GetLeftPart(UriPartial.Path));
        if ((uri.Host is "youtube.com" or "www.youtube.com") && (uri.AbsolutePath == "/watch" || uri.AbsolutePath.StartsWith("/live/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/@", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/channel/", StringComparison.Ordinal)))
            return new("YouTube", uri.GetLeftPart(UriPartial.Query));
        if (uri.Host == "youtu.be" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/[a-zA-Z0-9_-]{11}$"))
            return new("YouTube", uri.GetLeftPart(UriPartial.Path));
        return null;
    }
}

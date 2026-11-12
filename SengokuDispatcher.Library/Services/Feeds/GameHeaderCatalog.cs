namespace SengokuDispatcher.Library.Services.Feeds;

/// <summary>Artwork is served by Provider; unknown games retain a normal text heading.</summary>
public static class GameHeaderCatalog
{
    private static readonly IReadOnlyDictionary<int, (string ShortName, string File)> Games =
        new Dictionary<int, (string, string)>
        {
            [33945] = ("GGST", "33945.jpg"), [1386] = ("SSBU", "1386.jpg"),
            [43868] = ("SF6", "43868.jpg"), [49783] = ("TEKKEN 8", "49783.png"),
            [17] = ("TEKKEN 7", "17.jpg"), [4985] = ("TTT2", "4985.png"),
            [48548] = ("GBVSR", "48548.jpg"), [610] = ("3rd Strike", "610.jpg"),
            [36963] = ("KOF XV", "36963.jpg"), [5582] = ("SCII", "5582.jpg"),
            [48599] = ("MK1", "48599.jpg"), [287] = ("DBFZ", "287.jpg"), [1] = ("Melee", "1.jpg")
        };
    public static string? ShortName(int id) => Games.TryGetValue(id, out var game) ? game.ShortName : null;
    public static string? ImageUrl(int id, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("EventFeeds:AssetsBaseUrl must be a public HTTPS URL without credentials or query parameters.");
        return Games.TryGetValue(id, out var game) ? baseUrl.TrimEnd('/') + "/assets/game-headers/" + game.File : null;
    }
}

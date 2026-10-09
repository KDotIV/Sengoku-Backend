using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players;

public static class BracketCardBuilder
{
    public static int[] Build(BracketProcessingCheckpoint checkpoint, IEnumerable<LegendData> legends)
    {
        var byLink = legends.Where(x => x.PlayerLinkId > 0 && x.PlayerId > 0)
            .GroupBy(x => x.PlayerLinkId).ToDictionary(x => x.Key, x => x.First());
        var cards = checkpoint.Data.EntrantSetCards.ToDictionary(x => x.SetID);
        var missing = new HashSet<int>();
        var player = checkpoint.Data.PlayerTournamentCard;
        foreach (var opponent in checkpoint.ExpectedOpponents)
        {
            // Candidate matchups are not actual start.gg sets. Include both entrants
            // so multiple possible opponents in one round have different identities.
            var setId = $"br:{checkpoint.BracketId}:{opponent.PathSetId}:{player.EntrantID}:{opponent.EntrantId}";
            if (cards.ContainsKey(setId)) continue;
            if (!byLink.TryGetValue(opponent.PlayerLink, out var legend))
            {
                missing.Add(opponent.PlayerLink);
                continue;
            }
            cards[setId] = new EntrantSetCard
            {
                SetID = setId, PathStep = opponent.PathStep, PathSetId = opponent.PathSetId,
                EntrantOneID = player.EntrantID, PlayerOneID = player.PlayerID, EntrantOneName = player.PlayerName,
                EntrantTwoID = opponent.EntrantId, PlayerTwoID = legend.PlayerId, EntrantTwoName = opponent.GamerTag
            };
        }
        checkpoint.Data.EntrantSetCards = cards.Values.ToList();
        return missing.Order().ToArray();
    }
}

﻿namespace SengokuProvider.Library.Models.Events
{
    public class TournamentData
    {
        public required int Id { get; set; }
        public required string UrlSlug { get; set; }
        public int[]? Games { get; set; }
        public string[]? SocialLinks { get; set; }
        public string? MatcherinoSlug { get; set; }
        public string[]? ViewershipUrls { get; set; }
        public int[]? PlayerIDs { get; set; }
    }
}

﻿using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Events
{
    public class EventGraphQLResult
    {
        [JsonProperty("tournaments")]
        public required EventResult Events { get; set; }
    }

    public class EventResult
    {
        [JsonProperty("nodes")]
        public required List<EventNode> Nodes { get; set; }
        [JsonProperty("pageInfo")]
        public PageInfo? PageInfo { get; set; }
    }

    public class EventNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public required string Name { get; set; }

        [JsonProperty("addrState")]
        public string AddrState { get; set; } = string.Empty;

        [JsonProperty("lat")]
        public double? Lat { get; set; } = null;

        [JsonProperty("lng")]
        public double? Lng { get; set; } = null;

        [JsonProperty("venueAddress")]
        public string? VenueAddress { get; set; } = "default";

        [JsonProperty("startAt")]
        public long StartAt { get; set; }

        [JsonProperty("endAt")]
        public long EndAt { get; set; }
        [JsonProperty("slug")]
        public required string Slug { get; set; }
        [JsonProperty("events")]
        public List<TournamentDetails>? Tournaments { get; set; }
        [JsonProperty("registrationClosesAt")]
        public long RegistrationClosesAt { get; set; }
        [JsonProperty("isRegistrationOpen")]
        public bool IsRegistrationOpen { get; set; }
        [JsonProperty("isOnline")]
        public bool IsOnline { get; set; }
        [JsonProperty("city")]
        public string City { get; set; } = string.Empty;
    }
    public class TournamentDetails
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("videogame")]
        public Videogame? Videogame { get; set; }
        [JsonProperty("slug")]
        public string? UrlSlug { get; set; }
        [JsonProperty("numEntrants")]
        public int? NumEntrants { get; set; }
        [JsonProperty("tournament")]
        public EventNode? EventLink { get; set; }
    }

    public class Videogame
    {
        [JsonProperty("id")]
        public int Id { get; set; }
    }
    public class PageInfo
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("totalPages")]
        public int TotalPages { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("perPage")]
        public int PerPage { get; set; }

        [JsonProperty("sortBy")]
        public string? SortBy { get; set; }

        [JsonProperty("filter")]
        public string? Filter { get; set; }
    }
}

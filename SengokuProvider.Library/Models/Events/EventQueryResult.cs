﻿namespace SengokuProvider.Library.Models.Events
{
    public class AddressEventResult
    {
        public int EventId { get; set; }
        public required string Address { get; set; }
        public required double Latitude { get; set; }
        public required double Longitude { get; set; }
        public required double Distance { get; set; }
        public required string EventName { get; set; }
        public string? EventDescription { get; set; }
        public string? Region { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public required int LinkId { get; set; }
        public required DateTime ClosingRegistration { get; set; }
        public required string UrlSlug { get; set; }
        public required bool IsOnline { get; set; }
    }
}

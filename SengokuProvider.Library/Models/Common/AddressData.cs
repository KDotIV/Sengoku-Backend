﻿namespace SengokuProvider.Library.Models.Common
{
    public class AddressData
    {
        public int Id { get; set; }
        public string? Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? Distance { get; set; }
    }
}
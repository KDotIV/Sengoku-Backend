namespace SengokuProvider.Library.Models.Common
{
    public class AddressData
    {
        public int Id { get; set; }
        public required string Address { get; set; }
        public required double? Latitude { get; set; }
        public required double? Longitude { get; set; }
        public double? Distance { get; set; }
    }
}
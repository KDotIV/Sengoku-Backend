namespace SengokuProvider.API.Models.Common
{
    public class AddressData
    {
        public int Id { get; set; }
        public required string Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? Distance { get; set; }
    }
}
namespace SengokuProvider.API.Models.Common
{
    public class AddressData
    {
        public int Id { get; set; }
        public required string Address { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
    }
}
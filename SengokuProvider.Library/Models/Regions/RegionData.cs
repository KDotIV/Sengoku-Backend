namespace SengokuProvider.Library.Models.Regions
{
    public class RegionData
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required double Latitude { get; set; }
        public required double Longitude { get; set; }
        public required string Province { get; set; }
    }
}

namespace SengokuProvider.API.Models.Events
{
    public class EventData
    {
        public int Id { get; set; }
        public required string EventName { get; set; }
        public string? EventDescription { get; set; }
        public required int Region { get; set; }
        public int AddressID { get; set; }
        public required int LinkID { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}

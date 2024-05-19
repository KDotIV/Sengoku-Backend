namespace SengokuProvider.Library.Models.Events
{
    public class AddressEventResult
    {
        public required string Address { get; set; }
        public required double Latitude { get; set; }
        public required double Longitude { get; set; }
        public required double Distance { get; set; }
        public required string EventName { get; set; }
        public string? EventDescription { get; set; }
        public int Region { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public required int LinkId { get; set; }
        public required DateTime ClosingRegistration { get; set; }
    }
}

namespace SengokuProvider.Library.Models.Events
{
    public class EventData
    {
        public int Id { get; set; }
        public required string EventName { get; set; }
        public string? EventDescription { get; set; }
        public required int Region { get; set; }
        public required int AddressID { get; set; }
        public required int LinkID { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public required DateTime ClosingRegistration { get; set; }
        public required bool IsRegistrationOpen { get; set; }
    }
}

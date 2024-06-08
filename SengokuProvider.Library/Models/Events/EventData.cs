namespace SengokuProvider.Library.Models.Events
{
    public class EventData
    {
        public int Id { get; set; }
        public string? EventName { get; set; }
        public string? EventDescription { get; set; }
        public int Region { get; set; }
        public int AddressID { get; set; }
        public int LinkID { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime? ClosingRegistration { get; set; }
        public bool? IsRegistrationOpen { get; set; }
        public bool? IsOnline { get; set; }
        public string? UrlSlug { get; set; }
        public required DateTime LastUpdate { get; set; }
    }
}

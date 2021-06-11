using SQLite;

namespace GoldsparkIT.DnsBackend.Requests
{
    public class EventRequest
    {
        public NotifyTableChangedAction Action { get; set; }
        public string Type { get; set; }
        public string Data { get; set; }
    }
}
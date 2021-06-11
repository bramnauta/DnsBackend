namespace GoldsparkIT.DnsBackend.Requests
{
    public class AddToClusterRequest
    {
        public string Hostname { get; set; }
        public int Port { get; set; }
        public string ApiKey { get; set; }
    }
}
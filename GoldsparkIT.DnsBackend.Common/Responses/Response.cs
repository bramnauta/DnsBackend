// ReSharper disable InconsistentNaming

namespace GoldsparkIT.DnsBackend.Common.Responses
{
    public class Response
    {
        public Response()
        {
        }

        public Response(object result)
        {
            this.result = result;
        }

        public object result { get; set; }

        public static Response True => new(true);

        public static Response False => new(false);
    }
}
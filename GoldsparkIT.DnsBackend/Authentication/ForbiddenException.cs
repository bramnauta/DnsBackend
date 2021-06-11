using System;

namespace GoldsparkIT.DnsBackend.Authentication
{
    public class ForbiddenException : Exception
    {
        /// <inheritdoc />
        public override string Message => "Invalid API key or insufficient permissions";
    }
}
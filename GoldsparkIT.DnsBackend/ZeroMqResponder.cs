using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GoldsparkIT.DnsBackend.Common.Responses;
using GoldsparkIT.DnsBackend.Models;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;

namespace GoldsparkIT.DnsBackend
{
    public class ZeroMqResponder : JsonResponder
    {
        private readonly Thread _receiveThread;

        public ZeroMqResponder() : base(DbProvider.ProvideSQLiteConnection(true))
        {
            _receiveThread = new Thread(ReceiveThread);
        }

        public void Start()
        {
            _receiveThread.Start();
        }

        private void ReceiveThread()
        {
            var configuration = Db.Table<InternalConfiguration>().Single();
            using var server = new ResponseSocket();
            server.Bind($"tcp://127.0.0.1:{configuration.ZeroMqPort}");
            while (true)
            {
                ProcessMessage(server.ReceiveFrameString(), server);
            }
        }

        protected override Response Initialize(Dictionary<string, object> parameters)
        {
            return Response.True;
        }

        /// <inheritdoc />
        protected override void Send(Response data, object state)
        {
            ((ResponseSocket) state).SendFrame(JsonConvert.SerializeObject(data ?? Response.False));
        }
    }
}
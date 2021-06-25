using System;
using System.Threading;
using System.Threading.Tasks;
using GoldsparkIT.DnsBackend.Common.Requests;
using GoldsparkIT.DnsBackend.Common.Responses;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;

namespace GoldsparkIT.DnsBackend.Queryer
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            while (true)
            {
                using var socket = new RequestSocket("tcp://127.0.0.1:5237");

                var data = await Console.In.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(data))
                {
                    Thread.Sleep(1);
                    continue;
                }

                try
                {
                    ProcessRequest(data, socket);
                }
                catch (Exception ex)
                {
                    Log($"Exception: {ex}\r\nData: {data}\r\n");

                    Respond(JsonConvert.SerializeObject(Response.False));
                }
            }
        }

        private static void ProcessRequest(string data, RequestSocket socket)
        {
            Log($"{data}\r\n");

            var request = JsonConvert.DeserializeObject<Request>(data);

            socket.SendFrame(JsonConvert.SerializeObject(request));

            Respond(socket.ReceiveFrameString());
        }

        private static void Respond(string response)
        {
            Log($"Responding: {response}\r\n");
            Console.WriteLine(response);
        }

        private static void Log(string logData)
        {
            Console.Error.WriteLine(logData);
            Console.Error.Flush();
        }
    }
}
using System;
using System.Linq;
using System.Threading.Tasks;
using GoldsparkIT.DnsBackend.Models;
using Newtonsoft.Json;
using RestSharp;
using SQLite;

namespace GoldsparkIT.DnsBackend
{
    public class Synchronizer
    {
        private static Synchronizer _instance;
        private readonly IRestClient _client;

        private Synchronizer(IRestClient client)
        {
            _client = client;
        }

        public static Synchronizer Get()
        {
            return _instance ??= new Synchronizer(new RestClient());
        }

        public void Send(object obj, NotifyTableChangedAction action, SQLiteConnection db)
        {
            var dbWasOpen = db != null;
            if (!dbWasOpen)
            {
                db = DbProvider.ProvideSQLiteConnection();
            }

            var localNodeId = db.Table<InternalConfiguration>().Single().NodeId;
            var updateNodes = db.Table<Node>().Where(n => n.NodeId != localNodeId).ToList();
            var apiKey = db.Table<ApiKey>().First(k => k.ClusterKey).Key;

            Parallel.ForEach(updateNodes, node =>
            {
                try
                {
                    var req = new RestRequest($"http://{node.Hostname}:{node.Port}/sync/event");

                    req.AddHeader("X-Api-Key", apiKey);
                    req.AddJsonBody(new
                    {
                        action,
                        type = obj.GetType().Name,
                        Data = JsonConvert.SerializeObject(obj)
                    });

                    var response = _client.Put(req);

                    if (!response.IsSuccessful)
                    {
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Could not send update event to {node.Hostname}:{node.Port}:\r\n{ex.Message}\r\n{ex.StackTrace}");
                    // Ignore
                }
            });

            if (!dbWasOpen)
            {
                db.Close();
                db.Dispose();
            }
        }
    }
}
using System;
using SQLite;

namespace GoldsparkIT.DnsBackend.Models
{
    public class Node : BaseNode
    {
        public Node()
        {
        }

        public Node(BaseNode node)
        {
            Hostname = node.Hostname;
            Port = node.Port;
        }

        [PrimaryKey]
        public Guid Id { get; set; }

        [Indexed]
        public Guid NodeId { get; set; }
    }
}
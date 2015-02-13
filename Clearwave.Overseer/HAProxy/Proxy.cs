using System;
using System.Collections.Generic;
using System.Linq;

// Derived from: https://github.com/opserver/Opserver
namespace Clearwave.Overseer.HAProxy
{
    public class Proxy
    {
        public HAProxyServer Host { get; internal set; }
        public string Name { get; internal set; }
        public Frontend Frontend { get; internal set; }
        public List<Server> Servers { get; internal set; }
        public Backend Backend { get; internal set; }
        public DateTime PollDate { get; internal set; }

        public bool HasContent
        {
            get { return Frontend != null || Backend != null || (Servers != null && Servers.Count > 0); }
        }

        public bool HasFrontend { get { return Frontend != null; } }
        public bool HasServers { get { return Servers != null && Servers.Count > 0; } }
        public bool HasBackend { get { return Backend != null; } }

        private Item Primary { get { return (Item)Frontend ?? Backend; } }

        public string Status { get { return Primary.Status; } }
        public int LastStatusChangeSecondsAgo { get { return Primary.LastStatusChangeSecondsAgo; } }
        public long BytesIn { get { return Primary.BytesIn; } }
        public long BytesOut { get { return Primary.BytesIn; } }
    }
}
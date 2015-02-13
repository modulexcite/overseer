using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

// Derived from: https://github.com/opserver/Opserver
namespace Clearwave.Overseer.HAProxy
{
    public class HAProxyServer
    {
        public HAProxyServer(string statsHost)
        {
            this.StatsHost = statsHost;
            this.QueryTimeout = 5 * 1000;
        }

        public int QueryTimeout { get; set; }

        /// <summary>
        /// e.g. http://atl-lb01.prod.clearwaveinc.com:8080/stats
        /// </summary>
        public string StatsHost { get; private set; }

        public List<Proxy> FetchHAProxyStats()
        {
            string csv;
            var req = (HttpWebRequest)WebRequest.Create(StatsHost + ";csv");
            //req.Credentials = new NetworkCredential(User, Password);
            req.Timeout = QueryTimeout;
            using (var resp = req.GetResponse())
            using (var rs = resp.GetResponseStream())
            {
                if (rs == null)
                {
                    return null;
                }
                using (var sr = new StreamReader(rs))
                {
                    csv = sr.ReadToEnd();
                }
            }
            return ParseHAProxyStats(csv);
        }

        private List<Proxy> ParseHAProxyStats(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return new List<Proxy>();
            }
            using (var reader = new CSVReader(new StringReader(input), true))
            {
                var stats = new List<Item>();
                foreach (var row in reader)
                {
                    //Skip the header
                    if (row.Length == 0 || row[0].StartsWith("#"))
                    {
                        continue;
                    }
                    //Collect each stat line as we go, group later
                    stats.Add(Item.FromLine(row));
                }
                var result = stats.GroupBy(s => s.UniqueProxyId).Select(g => new Proxy
                {
                    Host = this,
                    Name = g.First().ProxyName,
                    Frontend = g.FirstOrDefault(s => s.Type == StatusType.Frontend) as Frontend,
                    Servers = g.OfType<Server>().ToList(),
                    Backend = g.FirstOrDefault(s => s.Type == StatusType.Backend) as Backend,
                    PollDate = DateTime.UtcNow
                }).ToList();

                return result;
            }
        }
    }
}

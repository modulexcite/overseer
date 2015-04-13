using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clearwave.Statsd;

namespace Clearwave.HAProxyTraffic
{
    public static class ConsolePrinter
    {
        static ConsolePrinter()
        {
            flushToConsole = bool.Parse(ConfigurationManager.AppSettings["haproxytraffic_FlushToConsole"]);
        }

        private static readonly bool flushToConsole;

        public static void Flush(long time_stamp, Metrics metrics)
        {
            if (!flushToConsole) { return; }

            Console.Clear();
            Console.WriteLine("statsd haproxy.logs: " + ExtensionMethods.UnixTimeStampToDateTime(time_stamp).ToString("O"));
            Console.WriteLine();
            if (!metrics.sets.ContainsKey("haproxy.logs.host")) { return; }
            Console.WriteLine("{0,10} {1,10} {2,7} {3,7} {4,6} {5,6} {6,6} {7,6}"
                        , "host"
                        , "route"
                        , "hits"
                        , "kb/sum"
                        , "tr/avg"
                        , "200/cnt"
                        , "300/cnt"
                        , "500/cnt"
                        );
            foreach (var host in metrics.sets["haproxy.logs.host"].OrderBy(x => x))
            {
                var hostClean = host.Replace('.', '_');
                foreach (var routeName in metrics.sets["haproxy.logs.routes"].OrderBy(x => x))
                {
                    var routeNameClean = routeName.Replace('.', '_');
                    Console.WriteLine("{0,10} {1,10} {2,7} {3,7:F0} {4,6} {5,6} {6,6} {7,6}"
                        , TrimAndPad(host, 10)
                        , TrimAndPad(routeName, 10)
                        , metrics.counters["haproxy.logs." + hostClean + ".route." + routeNameClean + ".hits"]
                        , (double)metrics.counters["haproxy.logs." + hostClean + ".route." + routeNameClean + ".bytes_read"] / 1024d
                        , metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["mean"]
                        , metrics.counters.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".status_code.200.hits")
                        , metrics.counters.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".status_code.300.hits")
                        , metrics.counters.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".status_code.500.hits")
                        );
                }
            }

            Console.WriteLine();

            foreach (var item in metrics.gauges.Where(x => x.Key.StartsWith("haproxy.logs.actconn")).OrderBy(x => x.Key))
            {
                Console.WriteLine(item.Key + "=" + item.Value);
            }
            foreach (var item in metrics.gauges.Where(x => x.Key.StartsWith("haproxy.logs.feconn")).OrderBy(x => x.Key))
            {
                Console.WriteLine(item.Key + "=" + item.Value);
            }
            foreach (var item in metrics.gauges.Where(x => x.Key.StartsWith("haproxy.logs.beconn")).OrderBy(x => x.Key))
            {
                Console.WriteLine(item.Key + "=" + item.Value);
            }
            foreach (var item in metrics.gauges.Where(x => x.Key.StartsWith("haproxy.logs.srv_conn")).OrderBy(x => x.Key))
            {
                Console.WriteLine(item.Key + "=" + item.Value);
            }

            Console.WriteLine();
            Console.WriteLine("haproxy.logs.queue=" + metrics.gauges["haproxy.logs.queue"]);
        }

        private static string TrimAndPad(string v, int len)
        {
            if (v.Length > len)
            {
                v = v.Substring(0, len);
            }
            return v.PadLeft(len);
        }

    }
}

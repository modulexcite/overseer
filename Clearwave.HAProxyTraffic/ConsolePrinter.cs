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

        private static readonly HashSet<string> EmptySet = new HashSet<string>();
        private static readonly Dictionary<string, long> EmptyTimerData = new Dictionary<string, long>() { { "mean", -1 } };

        public static void Flush(long time_stamp, Metrics metrics)
        {
            if (!flushToConsole) { return; }

            Console.Clear();
            Console.WriteLine("statsd haproxy.logs: " + ExtensionMethods.UnixTimeStampToDateTime(time_stamp).ToString("O"));
            Console.WriteLine();
            if (!metrics.sets.ContainsKey("haproxy.logs.host")) { return; }
            Console.WriteLine("{0,10} {1,5} {2,15} {3,7} {4,7:F0} {5,4} {6,5} {7,5} {8,5}"
                        , "host"
                        , "appid"
                        , "route"
                        , "hits"
                        , "kb/sum"
                        , "tr"
                        , "asp_d"
                        , "sql_c"
                        , "sql_d"
                        );

            var applications = metrics.sets.GetValueOrDefault("haproxy.logs.applications", EmptySet);
            foreach (var host in metrics.sets["haproxy.logs.host"].OrderBy(x => x))
            {
                var hostClean = host.Replace('.', '_');
                foreach (var routeName in metrics.sets["haproxy.logs.routes"].OrderBy(x => x))
                {
                    var applicationId = routeName.IndexOf(".") > 0 && applications.Contains(routeName.Substring(0, routeName.IndexOf("."))) ? routeName.Substring(0, routeName.IndexOf(".")) : "";
                    var routeNameClean = routeName.Replace('.', '_');
                    if (!metrics.counters.ContainsKey("haproxy.logs." + hostClean + ".route." + routeNameClean + ".hits"))
                    {
                        continue; // invalid route/host combo
                    }
                    Console.WriteLine("{0,10} {1,5} {2,15} {3,7} {4,7:F0} {5,4} {6,5} {7,5} {8,5}"
                        , TrimAndPad(host, 10)
                        , TrimAndPad(applicationId, 5)
                        , TrimAndPad(routeName.Replace(applicationId + ".", ""), 15)
                        , metrics.counters["haproxy.logs." + hostClean + ".route." + routeNameClean + ".hits"]
                        , (double)metrics.counters["haproxy.logs." + hostClean + ".route." + routeNameClean + ".bytes_read"] / 1024d
                        , metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["mean"]
                        , metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", EmptyTimerData)["mean"]
                        , metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", EmptyTimerData)["mean"]
                        , metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", EmptyTimerData)["mean"]
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

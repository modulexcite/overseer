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
            _flushToConsole = bool.Parse(ConfigurationManager.AppSettings["haproxytraffic_FlushToConsole"]);
        }

        private static readonly bool _flushToConsole;
        public static bool FlushToConsole { get { return _flushToConsole; } }

        private static readonly HashSet<string> EmptySet = new HashSet<string>();
        private static readonly Dictionary<string, long> EmptyTimerData = new Dictionary<string, long>() { { "median", 0 }, { "mean", 0 }, { "sum", 0 }, { "count_90", 0 }, { "mean_90", 0 }, { "sum_90", 0 } };

        public static void Flush(long time_stamp, Metrics metrics)
        {
            if (!FlushToConsole) { return; }

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

            Console.WriteLine("{0,10} {1,10} {2,10} {3,10} {4,10} {5,10} {6,10}"
                       , "actconn"
                       , "fe_name"
                       , "feconn"
                       , "be_name"
                       , "beconn"
                       , "srv_name"
                       , "srv_conn"
                       );

            if (metrics.gauges.ContainsKey("haproxy.logs.actconn"))
            {
                var actconn = metrics.gauges["haproxy.logs.actconn"];
                foreach (var frontend_name in metrics.sets["haproxy.logs.fe"].OrderBy(x => x))
                {
                    if (!metrics.gauges.ContainsKey("haproxy.logs.fe." + frontend_name + ".feconn")) { continue; }
                    var feconn = metrics.gauges["haproxy.logs.fe." + frontend_name + ".feconn"];
                    foreach (var backend_name in metrics.sets["haproxy.logs.be"].OrderBy(x => x))
                    {
                        if (!metrics.gauges.ContainsKey("haproxy.logs.fe." + frontend_name + ".be." + backend_name + ".beconn")) { continue; }
                        var beconn = metrics.gauges["haproxy.logs.fe." + frontend_name + ".be." + backend_name + ".beconn"];
                        foreach (var server_name in metrics.sets["haproxy.logs.srv"].OrderBy(x => x))
                        {
                            if (!metrics.gauges.ContainsKey("haproxy.logs.fe." + frontend_name + ".be." + backend_name + ".srv." + server_name + ".srv_conn")) { continue; }
                            var srv_conn = metrics.gauges["haproxy.logs.fe." + frontend_name + ".be." + backend_name + ".srv." + server_name + ".srv_conn"];
                            Console.WriteLine("{0,10} {1,10} {2,10} {3,10} {4,10} {5,10} {6,10}"
                               , actconn
                               , TrimAndPad(frontend_name, 10)
                               , feconn
                               , TrimAndPad(backend_name, 10)
                               , beconn
                               , TrimAndPad(server_name, 10)
                               , srv_conn
                               );
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("haproxy.logs.queue=" + metrics.gauges["haproxy.logs.queue"]);
            if (metrics.counters.ContainsKey("haproxy.logs.packets_received"))
            {
                Console.WriteLine("haproxy.logs.packets_received=" + metrics.counters["haproxy.logs.packets_received"]);
            }
            if (metrics.counters.ContainsKey("statsd.metrics_received"))
            {
                Console.WriteLine("statsd.metrics_received=" + metrics.counters["statsd.metrics_received"]);
            }
            if (metrics.gauges.ContainsKey("statsd.haproxy.databasewriter_duration"))
            {
                Console.WriteLine("statsd.haproxy.databasewriter_duration=" + metrics.gauges["statsd.haproxy.databasewriter_duration"]);
            }
            if (metrics.gauges.ContainsKey("statsd.flush_duration"))
            {
                Console.WriteLine("statsd.flush_duration=" + metrics.gauges["statsd.flush_duration"]);
            }
            if (metrics.gauges.ContainsKey("statsd.timestamp_lag_namespace"))
            {
                Console.WriteLine("statsd.timestamp_lag_namespace=" + metrics.gauges["statsd.timestamp_lag_namespace"]);
            }
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

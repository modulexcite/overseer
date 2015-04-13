using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clearwave.Statsd;

namespace Clearwave.HAProxyTraffic
{
    public static class TrafficLog
    {
        static TrafficLog()
        {
            collector = new StatsCollector();
            collector.FlushInterval = 60 * 1000; // 1 minute
            collector.FlushToConsole = bool.Parse(ConfigurationManager.AppSettings["statsd_FlushToConsole"]);
            collector.PctThreshold = new[] { 90 };
            collector.DeleteIdleStats = false;
            collector.BeforeFlush = () =>
            {
                collector.InReadLock(() =>
                {
                    collector.SetGauge("haproxy.logs.queue", queue.Count);
                    collector.IncrementMetricsReceived();
                });
            };
            collector.OnFlush = (time_stamp, metrics) =>
            {
                if (bool.Parse(ConfigurationManager.AppSettings["haproxytraffic_FlushToConsole"]))
                {
                    var trimPad = new Func<string, int, string>((v, len) =>
                    {
                        if (v.Length > len)
                        {
                            v = v.Substring(0, len);
                        }
                        return v.PadLeft(len);
                    });

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
                                , trimPad(host, 10)
                                , trimPad(routeName, 10)
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
            };
            collector.StartFlushTimer();
        }

        private static readonly StatsCollector collector;

        // TODO: this might not be the most performant compared to say... Disruptor pattern. But it's simple.
        private static readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent queueNotifier = new AutoResetEvent(false);

        public static void QueuePacket(string packet)
        {
            queue.Enqueue(packet);
            queueNotifier.Set();
        }


        public static void Start()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        string packet = "";
                        while (!queue.TryDequeue(out packet))
                        {
                            queueNotifier.WaitOne();
                        }
                        Handle(packet);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Exception Handling Packet: " + e.Message);
                    }
                }
            });
            Console.WriteLine("Started Traffic Log Aggregator Queue");
        }

        private static void Handle(string packet)
        {
            var log = haproxyRegex.Match(packet);
            if (log.Success)
            {
                var haproxy_name = log.Groups[3].Value;
                var client_ip = log.Groups[6].Value;
                var accept_date = DateTime.ParseExact(log.Groups[8].Value, "dd/MMM/yyyy:HH:mm:ss.fff", CultureInfo.InvariantCulture);

                var frontend_name = log.Groups[9].Value;
                var backend_name = log.Groups[10].Value;
                var server_name = log.Groups[11].Value;

                var timers = log.Groups[12].Value;
                var tr = int.Parse(timers.Split('/')[3]);

                var status_code = int.Parse(log.Groups[13].Value);
                var bytes_read = int.Parse(log.Groups[14].Value);

                var terminationState = log.Groups[17].Value;

                var conn = log.Groups[18].Value.Split('/');
                var actconn = int.Parse(conn[0]);
                var feconn = int.Parse(conn[1]);
                var beconn = int.Parse(conn[2]);
                var srv_conn = int.Parse(conn[3]);

                var captured_request_headers = log.Groups[20].Value.Split('|');
                var req_head_UserAgent = captured_request_headers[0];
                var req_head_Host = captured_request_headers[1];

                var captured_response_headers = log.Groups[21].Value.Split('|');
                var res_route_name = captured_response_headers[0];
                var res_sql_count = captured_response_headers[1];
                var res_sql_dur = captured_response_headers[2];
                var res_aspnet_dur = captured_response_headers[3];
                var res_app_id = captured_response_headers[4];

                var http_method = log.Groups[22].Value;
                var http_path = log.Groups[23].Value;

                var packetAge = DateTime.Now.Subtract(accept_date);
                if (packetAge.TotalMinutes > 1)
                {
                    // too old to flush? should we discard?
                    if (packetAge.TotalMinutes > 2)
                    {
                        // way too old to flush? should we discard?
                    }
                }

                int sql_count = 0;
                int sql_dur = 0;
                int aspnet_dur = 0;
                if (res_sql_count.Length > 0) { sql_count = int.Parse(res_sql_count); }
                if (res_sql_dur.Length > 0) { sql_dur = int.Parse(res_sql_dur); }
                if (res_aspnet_dur.Length > 0) { aspnet_dur = int.Parse(res_aspnet_dur); }

                collector.InReadLock(() =>
                {
                    if (!string.IsNullOrWhiteSpace(req_head_Host))
                    {
                        var hostClean = req_head_Host.Replace('.', '_');

                        collector.AddToSet("haproxy.logs.host", req_head_Host);
                        collector.AddToSet("haproxy.logs.routes", "_all");
                        collector.AddToCounter("haproxy.logs." + hostClean + ".route._all.hits", 1);
                        collector.AddToCounter("haproxy.logs." + hostClean + ".route._all.status_code." + status_code.ToString() + ".hits", 1);
                        collector.AddToCounter("haproxy.logs." + hostClean + ".route._all.bytes_read", bytes_read);
                        collector.AddToTimer("haproxy.logs." + hostClean + ".route._all.tr", tr);
                        collector.AddToCounter("haproxy.logs." + hostClean + ".route._all.SqlCount", sql_count);
                        collector.AddToTimer("haproxy.logs." + hostClean + ".route._all.SqlDurationMs", sql_dur);
                        collector.AddToTimer("haproxy.logs." + hostClean + ".route._all.AspNetDurationMs", aspnet_dur);
                        var metricCount = 9;

                        if (!string.IsNullOrWhiteSpace(res_route_name))
                        {
                            var routeName = res_route_name;
                            if (!string.IsNullOrWhiteSpace(res_app_id))
                            {
                                routeName = res_app_id + "." + routeName;
                            }
                            var routeNameClean = routeName.Replace('.', '_');
                            collector.AddToCounter("haproxy.logs." + hostClean + ".route." + routeNameClean + ".hits", 1);
                            collector.AddToCounter("haproxy.logs." + hostClean + ".route." + routeNameClean + ".status_code." + status_code.ToString() + ".hits", 1);
                            collector.AddToCounter("haproxy.logs." + hostClean + ".route." + routeNameClean + ".bytes_read", bytes_read);
                            collector.AddToTimer("haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr", tr);
                            collector.AddToCounter("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", sql_count);
                            collector.AddToTimer("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", sql_dur);
                            collector.AddToTimer("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", aspnet_dur);
                            collector.AddToSet("haproxy.logs.routes", routeName);
                            metricCount += 8;
                        }

                        collector.SetGauge("haproxy.logs.actconn", actconn);
                        collector.SetGauge("haproxy.logs.feconn." + frontend_name, feconn);
                        collector.SetGauge("haproxy.logs.beconn." + backend_name, beconn);
                        collector.SetGauge("haproxy.logs.srv_conn." + server_name, srv_conn);
                        metricCount += 4;

                        collector.IncrementMetricsReceived(metricCount);
                    }
                });
            }
        }

        //TrafficByRoute:
        //- key
        //Timestamp           datetime
        //RouteName           string
        //IsPageView          bit
        //ResponseCode        int
        //- stats
        //hits                int (just the sum)
        //bytes               int (sum, median, min, max, 90th)
        //tr                  int -- is the total time in milliseconds spent waiting for the server to send a full HTTP response, not counting data.
        //SqlCount            int (sum, median, min, max, 90th)
        //SqlDurationMs       int (sum, median, min, max, 90th)
        //AspNetDurationMs    int (sum, median, min, max, 90th)


        // Field   Format                                Extract from the example above
        // 1   process_name '[' pid ']:'                            haproxy[14389]:
        // 2   client_ip ':' client_port                             10.0.1.2:33317
        // 3   '[' accept_date ']'                       [06/Feb/2009:12:14:14.655]
        // 4   frontend_name                                                http-in
        // 5   backend_name '/' server_name                             static/srv1
        // 6   Tq '/' Tw '/' Tc '/' Tr '/' Tt*                       10/0/30/69/109
        // 7   status_code                                                      200
        // 8   bytes_read*                                                     2750
        // 9   captured_request_cookie                                            -
        // 10   captured_response_cookie                                           -
        // 11   termination_state                                               ----
        // 12   actconn '/' feconn '/' beconn '/' srv_conn '/' retries*    1/1/1/1/0
        // 13   srv_queue '/' backend_queue                                      0/0
        // 14   '{' captured_request_headers* '}'                   {haproxy.1wt.eu}
        // 15   '{' captured_response_headers* '}'                                {}
        // 16   '"' http_request '"'                      "GET /index.html HTTP/1.1"

        // Group 01 = <165>
        // Group 02 = Apr 10 13:40:03
        // Group 03 = atl-lb01.prod.clearwaveinc.com
        // Group 04 = haproxy
        // Group 05 = 13927
        // Group 06 = 70.88.217.41
        // Group 07 = 61709
        // Group 08 = 10/Apr/2015:08:09:20.593
        // Group 09 = http-web~
        // Group 10 = http-web
        // Group 11 = atl-web02
        // Group 12 = 50/0/0/32/82   = Tq '/' Tw '/' Tc '/' Tr '/' Tt*
        // Group 13 = 200  status_code
        // Group 14 = 354  bytes_read
        // Group 15 = -
        // Group 16 = -
        // Group 17 = ---- = termination_state
        // Group 18 = 932/918/4/4/0 = actconn '/' feconn '/' beconn '/' srv_conn '/' retries*
        // Group 19 = 0/0 = srv_queue '/' backend_queue
        // Group 20 = https://secure.clearwaveinc.com/v2.5/ProviderPortal/VisitList/Al|lla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko|secure.clearwaveinc.com|
        // Group 21 = {||||}
        // Group 22 = GET
        // Group 23 = /v2.5/ProviderPortal/VisitList/RefreshTabs?date=Fri+Apr+10+2015&fMultipleLocations=true&_=1428667760826
        // Group 24 = HTTP/1.1

        // request headers
        // capture request header User-Agent            len 128
        // capture request header Host                  len 64
        // capture request header X-Forwarded-For       len 64
        // capture response header X-Route-Name         len 96
        // capture response header X-Sql-Count          len 4
        // capture response header X-Sql-Duration-Ms    len 7
        // capture response header X-AspNet-Duration-Ms len 7
        // capture response header X-Application-Id     len 5



        private static Regex haproxyRegex =
            new Regex(
                @"^(<\d+>)(\w+ \d+ \S+) (\S+) (\S+)\[(\d+)\]: (\S+):(\d+) \[(\S+)\] (\S+) (\S+)\/(\S+) (\S+) (\S+) (\S+) *(\S+) (\S+) (\S+) (\S+) (\S+) \{([^}]*)\} \{([^}]*)\} ""(\S+) ([^""]+) (\S+)"".*$"
                , RegexOptions.Compiled);
    }
}

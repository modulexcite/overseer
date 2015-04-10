using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            collector.FlushToConsole = true;
            collector.PctThreshold = new[] { 90 };
            collector.DeleteIdleStats = false;
            interval = new Timer(state => collector.FlushMetrics(null), null, collector.FlushInterval, collector.FlushInterval);
            Console.WriteLine("Flushing every " + collector.FlushInterval + "ms");
        }

        private static readonly StatsCollector collector;
        static Timer interval; // need to keep a reference so GC doesn't clean it up

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
                var haproxy_name = log.Groups[4].Value;
                var client_ip = log.Groups[7].Value;
                var accept_date = DateTime.ParseExact(log.Groups[9].Value, "dd/MMM/yyyy:HH:mm:ss.fff", CultureInfo.InvariantCulture);
                var frontend_name = log.Groups[10].Value;
                var backend_name = log.Groups[11].Value;
                var server_name = log.Groups[12].Value;
                var timers = log.Groups[13].Value;
                var tr = int.Parse(timers.Split('/')[3]);
                var status_code = int.Parse(log.Groups[14].Value);
                var bytes_read = int.Parse(log.Groups[15].Value);
                var terminationState = log.Groups[18].Value;
                var req_head = log.Groups[21].Value;
                var res_head = log.Groups[22].Value;
                var http_method = log.Groups[23].Value;
                var http_path = log.Groups[24].Value;
                /*
                 * counters:
{route}.hits
{route}.{responsecode}.hits
timers:
{route}.bytes
{route}.tr
                 */

                collector.Handle("traffic._all.hits:1|c");
                collector.Handle("traffic._all." + status_code.ToString() + ".hits:1|c");
                collector.Handle("traffic._all.bytes_read:" + bytes_read + "|ms");
                collector.Handle("traffic._all.tr:" + tr + "|ms");
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
        // Group 03 = ATL-LB01
        // Group 04 = atl-lb01.prod.clearwaveinc.com
        // Group 05 = haproxy
        // Group 06 = 13927
        // Group 07 = 70.88.217.41
        // Group 08 = 61709
        // Group 09 = 10/Apr/2015:08:09:20.593
        // Group 10 = http-web~
        // Group 11 = http-web
        // Group 12 = atl-web02
        // Group 13 = 50/0/0/32/82   = Tq '/' Tw '/' Tc '/' Tr '/' Tt*
        // Group 14 = 200  status_code
        // Group 15 = 354  bytes_read
        // Group 16 = -
        // Group 17 = -
        // Group 18 = ---- = termination_state
        // Group 19 = 932/918/4/4/0 = actconn '/' feconn '/' beconn '/' srv_conn '/' retries*
        // Group 20 = 0/0 = srv_queue '/' backend_queue
        // Group 21 = https://secure.clearwaveinc.com/v2.5/ProviderPortal/VisitList/Al|lla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko|secure.clearwaveinc.com||gzip, deflate
        // Group 22 = gzip
        // Group 23 = GET
        // Group 24 = /v2.5/ProviderPortal/VisitList/RefreshTabs?date=Fri+Apr+10+2015&fMultipleLocations=true&_=1428667760826
        // Group 25 = HTTP/1.1


        private static Regex haproxyRegex =
            new Regex(
                @"^(<\d+>)(\w+ \d+ \S+) (\S+) (\S+) (\S+)\[(\d+)\]: (\S+):(\d+) \[(\S+)\] (\S+) (\S+)\/(\S+) (\S+) (\S+) (\S+) *(\S+) (\S+) (\S+) (\S+) (\S+) \{([^}]*)\} \{([^}]*)\} ""(\S+) ([^""]+) (\S+)"".*$"
                , RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}

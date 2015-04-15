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
            collector.FlushToConsole = false;
            collector.PctThreshold = new[] { 90 };
            collector.DeleteIdleStats = false;
            collector.DeleteGauges = true;
            collector.BeforeFlush += () =>
            {
                collector.InReadLock(() =>
                {
                    collector.SetGauge("haproxy.logs.queue", queue.Count);
                    collector.IncrementMetricsReceived();
                });
            };
            if (ConsolePrinter.FlushToConsole)
            {
                collector.OnFlush += ConsolePrinter.Flush;
            }
            if (DatabaseWriter.FlushToDatabase)
            {
                collector.OnFlush += DatabaseWriter.Flush;
            }
            collector.OnFlushError += (exception) =>
            {
                Program.Log.Error("Exception OnFlush(): ", exception);
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
                        ParsePacket(packet);
                    }
                    catch (Exception e)
                    {
                        Program.Log.Error("Exception Handling Packet: ", e);
                    }
                }
            });
            Program.Log.Info("Started Traffic Log Aggregator Queue");
        }

        private static Regex haproxyRegex =
            new Regex(
                @"^(<\d+>)(\w+ \d+ \S+) (\S+) (\S+)\[(\d+)\]: (\S+):(\d+) \[(\S+)\] (\S+) (\S+)\/(\S+) (\S+) (\S+) (\S+) *(\S+) (\S+) (\S+) (\S+) (\S+) \{([^}]*)\} \{([^}]*)\} ""(\S+) ([^""]+) (\S+)"".*$"
                , RegexOptions.Compiled);

        private static void ParsePacket(string packet)
        {
            var log = haproxyRegex.Match(packet);
            if (log.Success)
            {
                ProcessLog(log);
            }
        }

        private static void ProcessLog(Match log)
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

            int sql_count = -1;
            int sql_dur = -1;
            int aspnet_dur = -1;
            if (res_sql_count.Length > 0) { sql_count = int.Parse(res_sql_count); }
            if (res_sql_dur.Length > 0) { sql_dur = int.Parse(res_sql_dur); }
            if (res_aspnet_dur.Length > 0) { aspnet_dur = int.Parse(res_aspnet_dur); }

            // normalize statusCode into statusCode family
            var statusCode = status_code > 0 ? (int)(Math.Floor((double)status_code / 100d) * 100) : 0;

            collector.InReadLock(() =>
            {
                if (!string.IsNullOrWhiteSpace(req_head_Host))
                {
                    var hostClean = req_head_Host.Replace('.', '_');

                    collector.AddToSet("haproxy.logs.host", req_head_Host);
                    collector.AddToSet("haproxy.logs.routes", "_all");
                    if (!string.IsNullOrWhiteSpace(res_app_id))
                    {
                        collector.AddToSet("haproxy.logs.applications", res_app_id);
                    }
                    collector.AddToCounter("haproxy.logs." + hostClean + ".route._all.hits", 1);
                    collector.AddToCounter("haproxy.logs." + hostClean + ".route._all.status_code." + statusCode.ToString() + ".hits", 1);
                    collector.AddToCounter("haproxy.logs." + hostClean + ".route._all.bytes_read", bytes_read);
                    collector.AddToTimer("haproxy.logs." + hostClean + ".route._all.tr", tr);
                    if (sql_count >= 0)
                    {
                        collector.AddToTimer("haproxy.logs." + hostClean + ".route._all.SqlCount", sql_count);
                    }
                    if (sql_dur >= 0)
                    {
                        collector.AddToTimer("haproxy.logs." + hostClean + ".route._all.SqlDurationMs", sql_dur);
                    }
                    if (aspnet_dur >= 0)
                    {
                        collector.AddToTimer("haproxy.logs." + hostClean + ".route._all.AspNetDurationMs", aspnet_dur);
                    }
                    var metricCount = 9;

                    if (!string.IsNullOrWhiteSpace(res_route_name))
                    {
                        var routeName = http_method + "." + res_route_name;
                        if (!string.IsNullOrWhiteSpace(res_app_id))
                        {
                            routeName = res_app_id + "." + routeName;
                        }
                        var routeNameClean = routeName.Replace('.', '_');
                        collector.AddToCounter("haproxy.logs." + hostClean + ".route." + routeNameClean + ".hits", 1);
                        collector.AddToCounter("haproxy.logs." + hostClean + ".route." + routeNameClean + ".status_code." + statusCode.ToString() + ".hits", 1);
                        collector.AddToCounter("haproxy.logs." + hostClean + ".route." + routeNameClean + ".bytes_read", bytes_read);
                        collector.AddToTimer("haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr", tr);
                        if (sql_count >= 0)
                        {
                            collector.AddToTimer("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", sql_count);
                        }
                        if (sql_dur >= 0)
                        {
                            collector.AddToTimer("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", sql_dur);
                        }
                        if (aspnet_dur >= 0)
                        {
                            collector.AddToTimer("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", aspnet_dur);
                        }
                        collector.AddToSet("haproxy.logs.routes", routeName);
                        metricCount += 8;
                    }

                    collector.SetGauge("haproxy.logs.actconn", actconn);
                    metricCount += 1;
                    if (!string.IsNullOrWhiteSpace(frontend_name))
                    {
                        collector.AddToSet("haproxy.logs.fe", frontend_name);
                        collector.SetGauge("haproxy.logs.fe." + frontend_name + ".feconn", feconn);
                        metricCount += 2;
                        if (!string.IsNullOrWhiteSpace(backend_name))
                        {
                            collector.AddToSet("haproxy.logs.be", backend_name);
                            collector.SetGauge("haproxy.logs.fe." + frontend_name + ".be." + backend_name + ".beconn", beconn);
                            metricCount += 2;
                            if (!string.IsNullOrWhiteSpace(server_name))
                            {
                                collector.AddToSet("haproxy.logs.srv", server_name);
                                collector.SetGauge("haproxy.logs.fe." + frontend_name + ".be." + backend_name + ".srv." + server_name + ".srv_conn", srv_conn);
                                metricCount += 2;
                            }
                        }
                    }

                    collector.IncrementMetricsReceived(metricCount);
                }
            });
        }
    }
}

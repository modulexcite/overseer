using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clearwave.Statsd;
using Dapper;

namespace Clearwave.HAProxyTraffic
{
    public static class DatabaseWriter
    {
        public static string ConnectionString = ConfigurationManager.ConnectionStrings["TrafficDatabase"].ConnectionString;

        static DatabaseWriter()
        {
            _flushToDatabase = bool.Parse(ConfigurationManager.AppSettings["haproxytraffic_FlushToDatabase"]);
        }

        private static readonly bool _flushToDatabase;
        public static bool FlushToDatabase { get { return _flushToDatabase; } }

        private static readonly HashSet<string> EmptySet = new HashSet<string>();
        private static readonly Dictionary<string, long> EmptyTimerData = new Dictionary<string, long>() { { "median", 0 }, { "mean", 0 }, { "sum", 0 }, { "count_90", 0 }, { "mean_90", 0 }, { "sum_90", 0 } };

        private static SqlConnection GetOpenSqlConnection()
        {
            var conn = new SqlConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        [ThreadStatic]
        private static Stopwatch sw;

        public static void Flush(long time_stamp, Metrics metrics)
        {
            if (!FlushToDatabase) { return; }
            if (!metrics.sets.ContainsKey("haproxy.logs.host")) { return; }
            if (sw == null) { sw = new Stopwatch(); }
            sw.Restart();

            using (var c = GetOpenSqlConnection())
            {
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

                        var row = new
                        {
                            Timestamp                    = time_stamp,
                            Host                         = host,
                            ApplicationId                = applicationId,
                            RouteName                    = routeName,
                            Hits                         = metrics.counters["haproxy.logs." + hostClean + ".route." + routeNameClean + ".hits"],
                            BytesRead                    = metrics.counters["haproxy.logs." + hostClean + ".route." + routeNameClean + ".bytes_read"],
                            Tr_median                    = metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["median"],
                            Tr_mean                      = metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["mean"],
                            Tr_sum                       = metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["sum"],
                            Tr_count_90                  = metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["sum_90"] > 0 ?
                                                           metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["count_90"] : 0,
                            Tr_mean_90                   = metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["mean_90"],
                            Tr_sum_90                    = metrics.timer_data["haproxy.logs." + hostClean + ".route." + routeNameClean + ".tr"]["sum_90"],
                            AspNetDurationMs_median      = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", EmptyTimerData)["median"],
                            AspNetDurationMs_mean        = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", EmptyTimerData)["mean"],
                            AspNetDurationMs_sum         = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", EmptyTimerData)["sum"],
                            AspNetDurationMs_count_90    = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", EmptyTimerData)["sum_90"] > 0 ?
                                                           metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", EmptyTimerData)["count_90"] : 0,
                            AspNetDurationMs_mean_90     = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", EmptyTimerData)["mean_90"],
                            AspNetDurationMs_sum_90      = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".AspNetDurationMs", EmptyTimerData)["sum_90"],
                            SqlCount_median              = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", EmptyTimerData)["median"],
                            SqlCount_mean                = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", EmptyTimerData)["mean"],
                            SqlCount_sum                 = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", EmptyTimerData)["sum"],
                            SqlCount_count_90            = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", EmptyTimerData)["sum_90"] > 0 ?
                                                           metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", EmptyTimerData)["count_90"] : 0,
                            SqlCount_mean_90             = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", EmptyTimerData)["mean_90"],
                            SqlCount_sum_90              = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlCount", EmptyTimerData)["sum_90"],
                            SqlDurationMs_median         = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", EmptyTimerData)["median"],
                            SqlDurationMs_mean           = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", EmptyTimerData)["mean"],
                            SqlDurationMs_sum            = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", EmptyTimerData)["sum"],
                            SqlDurationMs_count_90       = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", EmptyTimerData)["sum_90"] > 0 ?
                                                           metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", EmptyTimerData)["count_90"] : 0,
                            SqlDurationMs_mean_90        = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", EmptyTimerData)["mean_90"],
                            SqlDurationMs_sum_90         = metrics.timer_data.GetValueOrDefault("haproxy.logs." + hostClean + ".route." + routeNameClean + ".SqlDurationMs", EmptyTimerData)["sum_90"],
                        };
                        InsertTrafficSummaryRow(row, c);
                    }
                }

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
                                var row = new
                                {
                                    Timestamp = time_stamp,
                                    Frontend = frontend_name,
                                    Backend = backend_name,
                                    Server = server_name,
                                    actconn = actconn,
                                    feconn = feconn,
                                    beconn = beconn,
                                    srv_conn = srv_conn,
                                };
                                InsertLoadBalancerStatisticsRow(row, c);
                            }
                        }
                    }
                }

                long packets_received = 0;
                long metrics_received = 0;
                long databasewriter_duration = 0;
                long flush_duration = 0;
                long timestamp_lag_namespace = 0;
                long queue_length = metrics.gauges["haproxy.logs.queue"];
                if (metrics.counters.ContainsKey("haproxy.logs.packets_received"))
                {
                    packets_received = metrics.counters["haproxy.logs.packets_received"];
                }
                if (metrics.counters.ContainsKey("statsd.metrics_received"))
                {
                    metrics_received =metrics.counters["statsd.metrics_received"];
                }
                if (metrics.gauges.ContainsKey("statsd.haproxy.databasewriter_duration"))
                {
                    databasewriter_duration = metrics.gauges["statsd.haproxy.databasewriter_duration"];
                }
                if (metrics.gauges.ContainsKey("statsd.flush_duration"))
                {
                    flush_duration = metrics.gauges["statsd.flush_duration"];
                }
                if (metrics.gauges.ContainsKey("statsd.timestamp_lag_namespace"))
                {
                    timestamp_lag_namespace = metrics.gauges["statsd.timestamp_lag_namespace"];
                }
                var stats_row = new
                {
                    Timestamp = time_stamp,
                    QueueLength = queue_length,
                    PacketsReceived = packets_received,
                    MetricsReceived = metrics_received,
                    DatabaseWriterDurationMS = databasewriter_duration,
                    FlushDurationMS = flush_duration,
                    TimestampLagNamespace = timestamp_lag_namespace,
                };
                InsertHAProxyTrafficLoggerStatisticsRow(stats_row, c);
            }
            sw.Stop();
            TrafficLog.collector.InReadLock(() =>
            {
                TrafficLog.collector.SetGauge("statsd.haproxy.databasewriter_duration", (int)Math.Round(sw.Elapsed.TotalMilliseconds));
            });
        }

        private static void InsertTrafficSummaryRow(object row, IDbConnection c)
        {
            c.Execute(InsertTrafficSummaryRowSQLStatement, param: row);
        }

        private const string InsertTrafficSummaryRowSQLStatement = @"
INSERT INTO [dbo].[TrafficSummary]
           ([Timestamp]
           ,[Host]
           ,[ApplicationId]
           ,[RouteName]
           ,[Hits]
           ,[BytesRead]
           ,[Tr_median]
           ,[Tr_mean]
           ,[Tr_sum]
           ,[Tr_count_90]
           ,[Tr_mean_90]
           ,[Tr_sum_90]
           ,[AspNetDurationMs_median]
           ,[AspNetDurationMs_mean]
           ,[AspNetDurationMs_sum]
           ,[AspNetDurationMs_count_90]
           ,[AspNetDurationMs_mean_90]
           ,[AspNetDurationMs_sum_90]
           ,[SqlCount_median]
           ,[SqlCount_mean]
           ,[SqlCount_sum]
           ,[SqlCount_count_90]
           ,[SqlCount_mean_90]
           ,[SqlCount_sum_90]
           ,[SqlDurationMs_median]
           ,[SqlDurationMs_mean]
           ,[SqlDurationMs_sum]
           ,[SqlDurationMs_count_90]
           ,[SqlDurationMs_mean_90]
           ,[SqlDurationMs_sum_90])
     VALUES
           (@Timestamp
           ,@Host
           ,@ApplicationId
           ,@RouteName
           ,@Hits
           ,@BytesRead
           ,@Tr_median
           ,@Tr_mean
           ,@Tr_sum
           ,@Tr_count_90
           ,@Tr_mean_90
           ,@Tr_sum_90
           ,@AspNetDurationMs_median
           ,@AspNetDurationMs_mean
           ,@AspNetDurationMs_sum
           ,@AspNetDurationMs_count_90
           ,@AspNetDurationMs_mean_90
           ,@AspNetDurationMs_sum_90
           ,@SqlCount_median
           ,@SqlCount_mean
           ,@SqlCount_sum
           ,@SqlCount_count_90
           ,@SqlCount_mean_90
           ,@SqlCount_sum_90
           ,@SqlDurationMs_median
           ,@SqlDurationMs_mean
           ,@SqlDurationMs_sum
           ,@SqlDurationMs_count_90
           ,@SqlDurationMs_mean_90
           ,@SqlDurationMs_sum_90)
";

        private static void InsertLoadBalancerStatisticsRow(object row, IDbConnection c)
        {
            c.Execute(InsertLoadBalancerStatisticsRowSQLStatement, param: row);
        }

        private const string InsertLoadBalancerStatisticsRowSQLStatement = @"
INSERT INTO [dbo].[LoadBalancerStatistics]
           ([Timestamp]
           ,[Frontend]
           ,[Backend]
           ,[Server]
           ,[actconn]
           ,[feconn]
           ,[beconn]
           ,[srv_conn])
     VALUES
           (@Timestamp
           ,@Frontend
           ,@Backend
           ,@Server
           ,@actconn
           ,@feconn
           ,@beconn
           ,@srv_conn)
";

        private static void InsertHAProxyTrafficLoggerStatisticsRow(object row, IDbConnection c)
        {
            c.Execute(InsertHAProxyTrafficLoggerStatisticsRowRowSQLStatement, param: row);
        }

        private const string InsertHAProxyTrafficLoggerStatisticsRowRowSQLStatement = @"
INSERT INTO [dbo].[HAProxyTrafficLoggerStatistics]
           ([Timestamp]
           ,[QueueLength]
           ,[PacketsReceived]
           ,[MetricsReceived]
           ,[DatabaseWriterDurationMS]
           ,[FlushDurationMS]
           ,[TimestampLagNamespace])
     VALUES
           (@Timestamp
           ,@QueueLength
           ,@PacketsReceived
           ,@MetricsReceived
           ,@DatabaseWriterDurationMS
           ,@FlushDurationMS
           ,@TimestampLagNamespace)
";

    }
}

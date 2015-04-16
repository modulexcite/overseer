using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace Clearwave.HAProxyTraffic
{
    public static class DatabaseRollup
    {
        public static string ConnectionString = ConfigurationManager.ConnectionStrings["TrafficDatabase"].ConnectionString;

        public const int RollupTimerIntervalMS = 60 * 15 * 1000; // 15 minutes

        public const int Level1RollupInterval = 60 * 10; // 10 minutes

        public const int Level1PruneInterval = 24 * 60 * 60; // 1 day

        private static Timer interval; // need to keep a reference so GC doesn't clean it up
        public static void StartRollupTimer()
        {
            if (interval != null)
            {
                return;
            }
            interval = new Timer(state =>
            {
                try
                {
                    ExecuteRollup();
                }
                catch (Exception e)
                {
                    Program.Log.Error("Error Executing Database Rollup", e);
                }
            }, null, 0, RollupTimerIntervalMS); // start rollup immediately
            Program.Log.Info("Started Database Rollup Timer: Every " + RollupTimerIntervalMS + "ms");
        }

        private static SqlConnection GetOpenSqlConnection()
        {
            var conn = new SqlConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        private static object rollupLock = new object();

        public static void ExecuteRollup()
        {
            lock (rollupLock)
            {
                Program.Log.Info("Executing Database Rollup");
                using (var c = GetOpenSqlConnection())
                {
                    Rollup("dbo.TrafficSummary", 1, c, TrafficSummaryInnerRollupSQL);
                    Program.Log.Info("Exec Rollup for dbo.TrafficSummary");
                    Rollup("dbo.LoadBalancerStatistics", 1, c, LoadBalancerStatisticsInnerRollupSQL);
                    Program.Log.Info("Exec Rollup for dbo.LoadBalancerStatistics");
                    Rollup("dbo.HAProxyTrafficLoggerStatistics", 1, c, HAProxyTrafficLoggerStatisticsInnerRollupSQL);
                    Program.Log.Info("Exec Rollup for dbo.HAProxyTrafficLoggerStatistics");
                }
                Program.Log.Info("Completed Database Rollup");
            }
        }

        private static void Rollup(string baseTableName, int level, IDbConnection c, string innerRollupSQL)
        {
            var sourceTableName = GetSourceTableName(baseTableName, level);
            var rollupTableName = GetRollupTableName(baseTableName, level);

            int? lastRollupTimestamp = GetLastRollupTimestamp(baseTableName, level, c);
            if (lastRollupTimestamp == null)
            {
                // never rolled up, get a new min timestamp
                lastRollupTimestamp = c.ExecuteScalar<int>("SELECT MIN(Timestamp) FROM " + sourceTableName);
                if (lastRollupTimestamp == null) { return; } // no rows, no work
                lastRollupTimestamp = RoundDownToNearestRollupStartTimestamp(lastRollupTimestamp.Value, level);
            }

            while (IsTimeToRollup(lastRollupTimestamp.Value, level)) // loop until we're completely rolled up
            {
                var maxTimstamp = lastRollupTimestamp.Value + GetLevelRollupInterval(level);
                using (var t = c.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    var sql = string.Format(OuterRollupSQL, string.Format(innerRollupSQL, rollupTableName, sourceTableName));
                    c.Execute(sql, param: new
                    {
                        minTS = lastRollupTimestamp.Value,
                        maxTS = maxTimstamp,
                        RollupTableName = rollupTableName,
                    }, transaction: t);
                    t.Commit();
                }
                lastRollupTimestamp = maxTimstamp; // next loop
            }
        }

        private const string LoadBalancerStatisticsInnerRollupSQL = @"
INSERT INTO {0}
SELECT
    COUNT(*) AS RollupCount
  , @minTS AS TimestampStart
  , @maxTS AS TimestampEnd
  , Frontend
  , Backend
  , [Server]
  , actconn  = MAX(actconn)
  , feconn   = MAX(feconn)
  , beconn   = MAX(beconn)
  , srv_conn = MAX(srv_conn)
FROM {1}
WHERE [Timestamp] >= @minTS
  AND [Timestamp] < @maxTS
GROUP BY Frontend, Backend, [Server]
HAVING COUNT(*) > 0;
";

        private const string HAProxyTrafficLoggerStatisticsInnerRollupSQL = @"
INSERT INTO {0}
SELECT
    COUNT(*) AS RollupCount
  , @minTS AS TimestampStart
  , @maxTS AS TimestampEnd
  , QueueLength              = MAX(QueueLength)
  , PacketsReceived          = SUM(PacketsReceived)
  , MetricsReceived          = SUM(MetricsReceived)
  , DatabaseWriterDurationMS = MAX(DatabaseWriterDurationMS)
  , FlushDurationMS          = MAX(FlushDurationMS)
  , TimestampLagNamespace    = SUM(TimestampLagNamespace)
FROM {1}
WHERE [Timestamp] >= @minTS
  AND [Timestamp] < @maxTS
HAVING COUNT(*) > 0;
";

        private const string TrafficSummaryInnerRollupSQL = @"
INSERT INTO {0}
SELECT
    COUNT(*) AS RollupCount
  , @minTS AS Timestamp
  , @maxTS AS TimestampEnd
  , Host
  , ApplicationId
  , RouteName
  , Hits                      = SUM(Hits)
  , BytesRead                 = SUM(BytesRead)
  , Tr_mean                   = IIF(SUM(Hits) > 0, (SUM(Tr_sum)/SUM(Hits)), 0)
  , Tr_sum                    = SUM(Tr_sum)
  , Tr_count_90               = SUM(Tr_count_90)
  , Tr_mean_90                = IIF(SUM(Tr_count_90) > 0, (SUM(Tr_sum_90)/SUM(Tr_count_90)), 0)
  , Tr_sum_90                 = SUM(Tr_sum_90)
  , AspNetDurationMs_mean     = IIF(SUM(Hits) > 0, (SUM(AspNetDurationMs_sum)/SUM(Hits)), 0)
  , AspNetDurationMs_sum      = SUM(AspNetDurationMs_sum)
  , AspNetDurationMs_count_90 = SUM(AspNetDurationMs_count_90)
  , AspNetDurationMs_mean_90  = IIF(SUM(AspNetDurationMs_count_90) > 0, (SUM(AspNetDurationMs_sum_90)/SUM(AspNetDurationMs_count_90)), 0)
  , AspNetDurationMs_sum_90   = SUM(AspNetDurationMs_sum_90)
  , SqlCount_mean             = IIF(SUM(Hits) > 0, (SUM(SqlCount_sum)/SUM(Hits)), 0)
  , SqlCount_sum              = SUM(SqlCount_sum)
  , SqlCount_count_90         = SUM(SqlCount_count_90)
  , SqlCount_mean_90          = IIF(SUM(SqlCount_count_90) > 0, (SUM(SqlCount_sum_90)/SUM(SqlCount_count_90)), 0)
  , SqlCount_sum_90           = SUM(SqlCount_sum_90)
  , SqlDurationMs_mean        = IIF(SUM(Hits) > 0, (SUM(SqlDurationMs_sum)/SUM(Hits)), 0)
  , SqlDurationMs_sum         = SUM(SqlDurationMs_sum)
  , SqlDurationMs_count_90    = SUM(SqlDurationMs_count_90)
  , SqlDurationMs_mean_90     = IIF(SUM(SqlDurationMs_count_90) > 0, (SUM(SqlDurationMs_sum_90)/SUM(SqlDurationMs_count_90)), 0)
  , SqlDurationMs_sum_90      = SUM(SqlDurationMs_sum_90)
FROM {1}
WHERE [Timestamp] >= @minTS
  AND [Timestamp] < @maxTS
GROUP BY Host, ApplicationId, RouteName
HAVING COUNT(*) > 0;
";

        private const string OuterRollupSQL = @"
BEGIN TRY
DECLARE @LastRollupStartTime datetime2 = GETUTCDATE();
DECLARE @LastRollupRecords int;

{0}

SELECT @LastRollupRecords = @@ROWCOUNT;

UPDATE dbo.HAProxyTrafficRollup
SET LastRollupTimestamp = @maxTS
  , LastRollupStartTime = @LastRollupStartTime
  , LastRollupEndTime = GETUTCDATE()
  , LastRollupRecords = @LastRollupRecords
WHERE RollupTableName = @RollupTableName;

END TRY
BEGIN CATCH
    DECLARE @Msg NVARCHAR(MAX)
    SELECT @Msg=ERROR_MESSAGE()
    RAISERROR('Error Occured: %s', 11, 1, @msg)
END CATCH
";

        private static bool IsTimeToRollup(int lastRollupTimestamp, int level)
        {
            var now = DateTime.UtcNow.DateTimeToUnixTimestamp();
            return lastRollupTimestamp < (now - GetLevelRollupInterval(level)); // ensure at least one interval's worth of time has passed since the last rollup
        }

        private static int GetLevelRollupInterval(int level)
        {
            if (level == 1)
            {
                return Level1RollupInterval;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private static int RoundDownToNearestRollupStartTimestamp(int ts, int level)
        {
            return ts - (ts % GetLevelRollupInterval(level));
        }

        private static int? GetLastRollupTimestamp(string baseTableName, int level, IDbConnection c)
        {
            return c.ExecuteScalar<int?>(@"
MERGE
    dbo.HAProxyTrafficRollup AS t 
USING
    (SELECT @RollupLevel AS [RollupLevel], @BaseTableName AS [BaseTableName]) AS s
 ON t.[RollupLevel]   = s.[RollupLevel]
AND t.[BaseTableName] = s.[BaseTableName]
WHEN MATCHED THEN
    UPDATE SET @RollupLevel=@RollupLevel
WHEN NOT MATCHED THEN
    INSERT ([BaseTableName], [RollupTableName], [SourceTableName], [RollupLevel]) VALUES (@BaseTableName, @RollupTableName, @SourceTableName, @RollupLevel)
OUTPUT inserted.LastRollupTimestamp;
", param: new
 {
     RollupTableName = new DbString { Value = GetRollupTableName(baseTableName, level), Length = 200, IsAnsi = true, },
     SourceTableName = new DbString { Value = GetSourceTableName(baseTableName, level), Length = 200, IsAnsi = true, },
     BaseTableName = new DbString { Value = baseTableName, Length = 200, IsAnsi = true, },
     RollupLevel = level,
 });
        }

        private static string GetRollupTableName(string baseTableName, int level)
        {
            return baseTableName + "RollupLevel" + level;
        }

        private static string GetSourceTableName(string baseTableName, int level)
        {
            if (level == 1)
            {
                return baseTableName;
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}

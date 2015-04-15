using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;

namespace Clearwave.Statsd
{
    public static class MetricsDatabase
    {
        public static string ConnectionString = ConfigurationManager.ConnectionStrings["MetricsDatabase"].ConnectionString;
        public static string SchemaName = ConfigurationManager.AppSettings["MetricsDatabase_Schema"];

        private static SqlConnection GetOpenSqlConnection()
        {
            var conn = new SqlConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        public static void RecordGauge(string key, long timestamp, long value)
        {
            using (var c = GetOpenSqlConnection())
            {
                var table = GetGaugeMetricTableName(key, c);
                c.Execute("INSERT INTO " + SchemaName + "." + table + " ([Timestamp], [Value]) VALUES (@t, @v)", new { t = timestamp, v = value });
            }
        }

        public static void RecordCounter(string key, long timestamp, long count, long rate)
        {
            using (var c = GetOpenSqlConnection())
            {
                var table = GetCounterMetricTableName(key, c);
                c.Execute("INSERT INTO " + SchemaName + "." + table + " ([Timestamp], [Count], [Rate]) VALUES (@t, @c, @r)", new { t = timestamp, c = count, r = rate });
            }
        }

        public static void RecordSet(string key, long timestamp, int count)
        {
            using (var c = GetOpenSqlConnection())
            {
                var table = GetSetMetricTableName(key, c);
                c.Execute("INSERT INTO " + SchemaName + "." + table + " ([Timestamp], [Count]) VALUES (@t, @c)", new { t = timestamp, c = count });
            }
        }

        public static void RecordTimer(string key, long timestamp, IDictionary<string, long> values)
        {
            if (values["count"] == 0)
            {
                return; // don't record
            }
            using (var c = GetOpenSqlConnection())
            {
                var table = GetTimerMetricTableName(key, c);
                c.Execute(@"INSERT INTO " + SchemaName + "." + table + @"
([Timestamp]
,[count]
,[rate]
,[sum]
,[sum_squares]
,[mean]
,[median]
,[upper]
,[lower]
,[stddev]
,[count_90]
,[mean_90]
,[upper_90]
,[sum_90]
,[sum_squares_90])
VALUES
(@Timestamp
,@count
,@rate
,@sum
,@sum_squares
,@mean
,@median
,@upper
,@lower
,@stddev
,@count_90
,@mean_90
,@upper_90
,@sum_90
,@sum_squares_90);"
                    , new
                    {
                        Timestamp = timestamp,
                        count = values["count"],
                        rate = values["count_ps"],
                        sum = values["sum"],
                        sum_squares = values["sum_squares"],
                        mean = values["mean"],
                        median = values["median"],
                        upper = values["upper"],
                        lower = values["lower"],
                        stddev = values["std"],
                        count_90 = values["count_90"],
                        mean_90 = values["mean_90"],
                        upper_90 = values["upper_90"],
                        sum_90 = values["sum_90"],
                        sum_squares_90 = values["sum_squares_90"],
                    });
            }
        }

        public static string GetGaugeMetricTableName(string key, IDbConnection c)
        {
            var metricId = GetMetricID("stats.gauges." + key, "gauge", c);
            var tableName = GetMetricTableName(metricId, "g_", c);

            c.Execute(@"
IF (SELECT OBJECT_ID('" + SchemaName + @"." + tableName + @"')) IS NULL
CREATE TABLE " + SchemaName + @"." + tableName + @"
(
    [Timestamp] int NOT NULL,
    [Value] int,
    CONSTRAINT [PK_" + tableName + @"] PRIMARY KEY CLUSTERED
    (
        [Timestamp] ASC
    )
)");
            return tableName;
        }

        public static string GetCounterMetricTableName(string key, IDbConnection c)
        {
            var metricId = GetMetricID("stats.counters." + key, "counter", c);
            var tableName = GetMetricTableName(metricId, "c_", c);

            c.Execute(@"
IF (SELECT OBJECT_ID('" + SchemaName + @"." + tableName + @"')) IS NULL
CREATE TABLE " + SchemaName + @"." + tableName + @"
(
    [Timestamp] int NOT NULL,
    [Count] int,
    [Rate] int,
    CONSTRAINT [PK_" + tableName + @"] PRIMARY KEY CLUSTERED
    (
        [Timestamp] ASC
    )
)");
            return tableName;
        }

        public static string GetSetMetricTableName(string key, IDbConnection c)
        {
            var metricId = GetMetricID("stats.sets." + key, "set", c);
            var tableName = GetMetricTableName(metricId, "s_", c);

            c.Execute(@"
IF (SELECT OBJECT_ID('" + SchemaName + @"." + tableName + @"')) IS NULL
CREATE TABLE " + SchemaName + @"." + tableName + @"
(
    [Timestamp] int NOT NULL,
    [Count] int,
    CONSTRAINT [PK_" + tableName + @"] PRIMARY KEY CLUSTERED
    (
        [Timestamp] ASC
    )
)");
            return tableName;
        }

        public static string GetTimerMetricTableName(string key, IDbConnection c)
        {
            var metricId = GetMetricID("stats.timers." + key, "timer", c);
            var tableName = GetMetricTableName(metricId, "t_", c);

            c.Execute(@"
IF (SELECT OBJECT_ID('" + SchemaName + @"." + tableName + @"')) IS NULL
CREATE TABLE " + SchemaName + @"." + tableName + @"
(
    [Timestamp] int NOT NULL,
    [count] int,
    [rate] int,
    [sum] bigint,
    [sum_squares] bigint,
    [mean] int,
    [median] int,
    [upper] int,
    [lower] int,
    [stddev] int,
    [count_90] int,
    [mean_90] int,
    [upper_90] int,
    [sum_90] bigint,
    [sum_squares_90] bigint,
    CONSTRAINT [PK_" + tableName + @"] PRIMARY KEY CLUSTERED
    (
        [Timestamp] ASC
    )
)");
            return tableName;
        }

        private static string GetMetricTableName(int metricID, string tableNamePrefix, IDbConnection c)
        {
            var tableName = c.ExecuteScalar<string>(@"
MERGE
    " + SchemaName + @".MetricTable AS t 
USING
    (SELECT @MetricId AS MetricID) AS s
ON t.MetricID = s.MetricID
WHEN MATCHED THEN
    UPDATE SET t.TableName = @prefix + CAST(s.MetricID AS VARCHAR(10))
WHEN NOT MATCHED THEN
    INSERT (MetricID, TableName) VALUES (s.MetricID, @prefix + CAST(s.MetricID AS VARCHAR(10)))
OUTPUT inserted.TableName;
", new { MetricId = metricID, prefix = new DbString { Value = tableNamePrefix, Length = 2, IsAnsi = true, } });

            c.ExecuteScalar<string>(@"
MERGE
    " + SchemaName + @".MetricTableDataRollupLevel AS t 
USING
    (SELECT @MetricId AS MetricID, 1 AS [Level]) AS s
ON t.MetricID = s.MetricID AND t.[Level] = s.[Level]
WHEN NOT MATCHED THEN
    INSERT (MetricID, [Level], RollupTableName) VALUES (s.MetricID, 1, @prefix + CAST(s.MetricID AS VARCHAR(10)) + '_RollupLevel1');
", new { MetricId = metricID, prefix = new DbString { Value = tableNamePrefix, Length = 2, IsAnsi = true, } });

            return tableName;
        }

        private static int GetMetricID(string key, string type, IDbConnection c)
        {
            var metricId = c.ExecuteScalar<int>(@"
MERGE
    " + SchemaName + @".Metric AS t 
USING
    (SELECT @Key AS [Key], @Type AS [Type]) AS s
ON t.[Key] = s.[Key]
WHEN MATCHED THEN
    UPDATE SET t.[Key] = s.[Key]
WHEN NOT MATCHED THEN
    INSERT ([Key], [Type]) VALUES (s.[Key], s.[Type])
OUTPUT inserted.MetricID;
", new { Key = key, Type = type });
            return metricId;
        }

        public static void Rollup()
        {
            /*
             * Here's the idea:
             * 
             * 1. This table will keep track of which metrics have been rolled up, when, etc.
             * 2. On an interval, for each row:
             *  a. figure out if the "LastRollupTimestamp" is far enough in the past to
             *     rollup again
             *  b. starting at "LastRollupTimestamp", execute one or more rollups inside
             *     a transaction
             *  c. update the MetricTableDataRollupLevel row and commit
             * 3. We can do the same thing for purging data.
             * 
             * CREATE TABLE [dbo].[MetricTableDataRollupLevel] (
             *     [MetricID] [int] NOT NULL,
             *     [Level] [tinyint] NOT NULL,
             *     [RollupTableName] varchar(200) NOT NULL,
             *     [LastRollupTimestamp] [int] NULL,
             *     [LastRollupStartTime] [datetime] NULL,
             *     [LastRollupEndTime] [datetime] NULL,
             *     [LastRollupRecords] [int] NULL,
             *     [LastPurgeBeforeTimestamp] [int] NULL,
             *     [LastPurgeStartTime] [datetime] NULL,
             *     [LastPurgeEndTime] [datetime] NULL,
             *     [LastPurgeRecords] [bigint] NULL,
             *     CONSTRAINT [PK_MetricTableDataRollupLevel] PRIMARY KEY CLUSTERED 
             *     (
             *         [MetricID], [Level] ASC
             *     )
             * )
             */
        }
    }
}

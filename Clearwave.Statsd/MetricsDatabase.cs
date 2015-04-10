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

        public static void RecordGauge(string key, int timestamp, long value)
        {
            using (var c = GetOpenSqlConnection())
            {
                var table = GetGaugeMetricTableName(key, c);
                c.Execute("INSERT INTO " + SchemaName + "." + table + " ([Timestamp], [Value]) VALUES (@t, @v)", new { t = timestamp, v = value });
            }
        }

        public static string GetGaugeMetricTableName(string key, IDbConnection c)
        {
            var metricId = GetMetricID(key, c);

            var tableName = c.ExecuteScalar<string>(@"
MERGE
    " + SchemaName + @".MetricTable AS t 
USING
    (SELECT @MetricId AS MetricID) AS s
ON t.MetricID = s.MetricID
WHEN MATCHED THEN
    UPDATE SET t.TableName = 'g_' + CAST(s.MetricID AS VARCHAR(10))
WHEN NOT MATCHED THEN
    INSERT (MetricID, TableName) VALUES (s.MetricID, 'g_' + CAST(s.MetricID AS VARCHAR(10)))
OUTPUT inserted.TableName;
", new { MetricId = metricId });

            c.Execute(@"
IF (SELECT OBJECT_ID('" + SchemaName + @"." + tableName + @"')) IS NULL
CREATE TABLE " + SchemaName + @"." + tableName + @"
(
    [Timestamp] int NOT NULL,
    [Value] float
)");

            return tableName;
        }

        private static int GetMetricID(string key, IDbConnection c)
        {
            var metricId = c.ExecuteScalar<int>(@"
MERGE
    dbo.Metric AS t 
USING
    (SELECT @Key AS [Key]) AS s
ON t.[Key] = s.[Key]
WHEN MATCHED THEN
    UPDATE SET t.[Key] = s.[Key]
WHEN NOT MATCHED THEN
    INSERT ([Key]) VALUES (s.[Key])
OUTPUT inserted.MetricID;
", new { Key = key });
            return metricId;
        }
    }
}

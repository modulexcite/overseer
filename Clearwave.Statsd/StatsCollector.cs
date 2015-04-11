﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Clearwave.Statsd
{
    public class StatsCollector
    {
        public StatsCollector()
        {
            FlushInterval = 10 * 1000;
            PctThreshold = new[] { 90 };
            FlushToConsole = false;

            DeleteIdleStats = false;
            DeleteCounters = false;
            DeleteTimers = false;
            DeleteSets = false;
            DeleteGauges = false;
        }

        /// <summary>
        /// for time information, calculate the Nth percentile(s)
        /// (can be a single value or list of floating-point values)
        /// negative values mean to use "top" Nth percentile(s) values
        /// [%, default: 90]
        /// </summary>
        public int[] PctThreshold { get; set; }
        /// <summary>
        /// interval (in ms) to flush metrics to each backend - default is 10,000ms
        /// </summary>
        public int FlushInterval { get; set; }
        public bool FlushToConsole { get; set; }
        /// <summary>
        /// don't send values to graphite for inactive counters, sets, gauges, or timers as opposed to sending 0.
        /// </summary>
        public bool DeleteIdleStats { get; set; }
        public bool DeleteCounters { get; set; }
        public bool DeleteTimers { get; set; }
        public bool DeleteSets { get; set; }
        public bool DeleteGauges { get; set; }

        private Dictionary<string, long> counters = new Dictionary<string, long>()
        { 
            { "statsd.packets_received", 0 },
            { "statsd.metrics_received", 0 },
            { "statsd.bad_lines_seen", 0 }
        };

        private Dictionary<string, List<long>> timers = new Dictionary<string, List<long>>();
        private Dictionary<string, long> timer_counters = new Dictionary<string, long>();
        private Dictionary<string, long> gauges = new Dictionary<string, long>();
        private Dictionary<string, HashSet<string>> sets = new Dictionary<string, HashSet<string>>();

        private readonly ReaderWriterLockSlim flushMetricsReaderWriterLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        private long old_timestamp = 0;

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
        public static double DateTimeToUnixTimestamp(DateTime dateTime)
        {
            return (dateTime - Epoch.ToLocalTime()).TotalSeconds;
        }
        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            return Epoch.AddSeconds(unixTimeStamp).ToLocalTime();
        }

        public void StartFlushTimer()
        {
            if (interval != null)
            {
                return;
            }
            interval = new Timer(state => FlushMetrics(), null, FlushInterval, FlushInterval);
            Console.WriteLine("Flushing every " + FlushInterval + "ms");
        }

        private Timer interval; // need to keep a reference so GC doesn't clean it up


        public Action BeforeFlush { get; set; }
        public Action<long, Metrics> OnFlush { get; set; }

        public void FlushMetrics()
        {
            if (BeforeFlush != null)
            {
                BeforeFlush();
            }
            var time_stamp = (long)Math.Round(DateTimeToUnixTimestamp(DateTime.UtcNow)); // seconds
            if (old_timestamp > 0)
            {
                gauges["statsd.timestamp_lag_namespace"] = (time_stamp - old_timestamp - (FlushInterval / 1000));
            }
            old_timestamp = time_stamp;

            Metrics metrics = null;
            flushMetricsReaderWriterLock.EnterWriteLock();
            try
            {
                metrics = new Metrics
                {
                    counters = new Dictionary<string, long>(counters),
                    gauges = new Dictionary<string, long>(gauges),
                    timers = new Dictionary<string, List<long>>(timers),
                    timer_counters = new Dictionary<string, long>(timer_counters),
                    sets = new Dictionary<string, HashSet<string>>(sets),
                    counter_rates = new Dictionary<string, double>(),
                    timer_data = new Dictionary<string, Dictionary<string, long>>(),
                    pctThreshold = PctThreshold,
                    statsd_metrics = new Dictionary<string, long>(),
                };
                ClearMetrics();
            }
            finally
            {
                flushMetricsReaderWriterLock.ExitWriteLock();
            }

            ProcessMetrics(metrics, FlushInterval, time_stamp);

            if (FlushToConsole)
            {
                Console.Clear();
                Console.WriteLine("Flush=" + time_stamp);
                foreach (var item in metrics.counters)
                {
                    Console.WriteLine("stats.counters.{0}.count = {1}", item.Key, item.Value);
                    Console.WriteLine("stats.counters.{0}.rate = {1}", item.Key, metrics.counter_rates[item.Key]);
                }
                foreach (var item in metrics.timers)
                {
                    foreach (var data in metrics.timer_data[item.Key])
                    {
                        Console.WriteLine("stats.timers.{0}.{1} = {2}", item.Key, data.Key, data.Value);
                    }
                }
                foreach (var item in metrics.gauges)
                {
                    Console.WriteLine("stats.gauges.{0} = {1}", item.Key, item.Value);
                }
                foreach (var item in metrics.sets)
                {
                    Console.WriteLine("stats.sets.{0}.count = {1}", item.Key, item.Value.Count);
                }
                Console.WriteLine("Flush End=" + time_stamp);
            }

            if (OnFlush != null)
            {
                OnFlush(time_stamp, metrics);
            }
        }

        private void ClearMetrics()
        {
            var deleteCounters = DeleteIdleStats ? true : DeleteCounters;
            var deleteTimers = DeleteIdleStats ? true : DeleteTimers;
            var deleteSets = DeleteIdleStats ? true : DeleteSets;
            var deleteGauges = DeleteIdleStats ? true : DeleteGauges;

            foreach (var key in counters.Keys.ToList())
            {
                if (key == "statsd.packets_received" ||
                    key == "statsd.metrics_received" ||
                    key == "statsd.bad_lines_seen")
                {
                    counters[key] = 0;
                    continue;
                }
                if (deleteCounters)
                {
                    counters.Remove(key);
                }
                else
                {
                    counters[key] = 0;
                }
            }

            foreach (var key in timers.Keys.ToList())
            {
                if (deleteTimers)
                {
                    timers.Remove(key);
                    timer_counters.Remove(key);
                }
                else
                {
                    timers[key] = new List<long>();
                    timer_counters[key] = 0;
                }
            }

            foreach (var key in sets.Keys.ToList())
            {
                if (deleteSets)
                {
                    sets.Remove(key);
                }
                else
                {
                    sets[key] = new HashSet<string>();
                }
            }

            foreach (var key in gauges.Keys.ToList())
            {
                if (deleteGauges)
                {
                    gauges.Remove(key);
                }
            }
        }

        public static void ProcessMetrics(Metrics metrics, double flushInterval, long ts)
        {
            var sw = Stopwatch.StartNew();
            var counter_rates = (Dictionary<string, double>)metrics.counter_rates;
            var timer_data = (Dictionary<string, Dictionary<string, long>>)metrics.timer_data;
            var statsd_metrics = (Dictionary<string, long>)metrics.statsd_metrics;
            var counters = (Dictionary<string, long>)metrics.counters;
            var timers = (Dictionary<string, List<long>>)metrics.timers;
            var timer_counters = (Dictionary<string, long>)metrics.timer_counters;
            var pctThreshold = (int[])metrics.pctThreshold;
            //var histogram = metrics.histogram;

            foreach (var key in counters.Keys)
            {
                var value = (double)counters[key];

                // calculate "per second" rate
                counter_rates[key] = Math.Round(value / (flushInterval / 1000d));
            }

            foreach (var key in timers.Keys)
            {
                var current_timer_data = new Dictionary<string, long>();
                timer_data[key] = current_timer_data;

                if (timers[key].Count > 0)
                {
                    var values = timers[key];
                    values.Sort();
                    var count = values.Count;
                    var min = values[0];
                    var max = values[count - 1];

                    var cumulativeValues = new List<long>() { min };
                    var cumulSumSquaresValues = new List<long>() { min * min };
                    for (var i = 1; i < count; i++)
                    {
                        cumulativeValues.Add(values[i] + cumulativeValues[i - 1]);
                        cumulSumSquaresValues.Add((values[i] * values[i]) + cumulSumSquaresValues[i - 1]);
                    }

                    var sum = min;
                    var sumSquares = min * min;
                    var mean = min;
                    var thresholdBoundary = max;


                    foreach (var pct in pctThreshold)
                    {
                        var numInThreshold = count;

                        if (count > 1)
                        {
                            numInThreshold = (int)Math.Round(((double)Math.Abs(pct) / 100d) * (double)count);
                            if (numInThreshold == 0)
                            {
                                continue;
                            }

                            if (pct > 0)
                            {
                                thresholdBoundary = values[numInThreshold - 1];
                                sum = cumulativeValues[numInThreshold - 1];
                                sumSquares = cumulSumSquaresValues[numInThreshold - 1];
                            }
                            else
                            {
                                thresholdBoundary = values[count - numInThreshold];
                                sum = cumulativeValues[count - 1] - cumulativeValues[count - numInThreshold - 1];
                                sumSquares = cumulSumSquaresValues[count - 1] -
                                  cumulSumSquaresValues[count - numInThreshold - 1];
                            }
                            mean = (long)Math.Round((double)sum / (double)numInThreshold);
                        }

                        var clean_pct = "" + pct;
                        clean_pct = clean_pct.Replace(".", "_").Replace("-", "top");
                        current_timer_data["count_" + clean_pct] = numInThreshold;
                        current_timer_data["mean_" + clean_pct] = mean;
                        current_timer_data[(pct > 0 ? "upper_" : "lower_") + clean_pct] = thresholdBoundary;
                        current_timer_data["sum_" + clean_pct] = sum;
                        current_timer_data["sum_squares_" + clean_pct] = sumSquares;
                    }

                    sum = cumulativeValues[count - 1];
                    sumSquares = cumulSumSquaresValues[count - 1];
                    mean = (long)Math.Round((double)sum / (double)count);

                    long sumOfDiffs = 0;
                    for (var i = 0; i < count; i++)
                    {
                        sumOfDiffs += (values[i] - mean) * (values[i] - mean);
                    }

                    var mid = (int)Math.Floor((double)count / 2d);
                    var median = (count % 2) > 0 ? values[mid] : (values[mid - 1] + values[mid]) / 2;

                    var stddev = Math.Sqrt(Math.Round((double)sumOfDiffs / (double)count));
                    current_timer_data["std"] = (long)stddev;
                    current_timer_data["upper"] = max;
                    current_timer_data["lower"] = min;
                    current_timer_data["count"] = timer_counters[key];
                    current_timer_data["count_ps"] = (long)Math.Round((double)timer_counters[key] / (flushInterval / 1000d));
                    current_timer_data["sum"] = sum;
                    current_timer_data["sum_squares"] = sumSquares;
                    current_timer_data["mean"] = mean;
                    current_timer_data["median"] = median;

                }
                else
                {
                    current_timer_data["count"] = 0;
                    current_timer_data["count_ps"] = 0;
                }
            }

            sw.Stop();
            statsd_metrics["processing_time"] = (int)Math.Round(sw.Elapsed.TotalSeconds);
        }

        public void Handle(string packet_data)
        {
            flushMetricsReaderWriterLock.EnterReadLock();
            try
            {
                // Guages
                // <metric name>:<value>|g
                // 
                // Counters
                // <metric name>:<value>|c[|@<sample rate>]
                // 
                // Timers
                // <metric name>:<value>|ms
                // 
                // Histograms
                // <metric name>:<value>|h
                // 
                // Meters
                // <metric name>:<value>|m

                counters["statsd.packets_received"]++;

                string[] metrics = null;
                if (packet_data.IndexOf("\n") > -1)
                {
                    metrics = packet_data.Split('\n');
                }
                else
                {
                    metrics = new string[] { packet_data };
                }
                for (int midx = 0; midx < metrics.Length; midx++)
                {
                    if (metrics[midx].Length == 0)
                    {
                        continue;
                    }

                    counters["statsd.metrics_received"]++;
                    var bits = metrics[midx].ToString().Split(':');
                    var key = bits[0];

                    var sampleRate = 1d;
                    var fields = bits[1].Split('|');
                    if (!IsValidPacket(fields))
                    {
                        counters["statsd.bad_lines_seen"]++;
                        continue;
                    }
                    if (fields.Length >= 3)
                    {
                        sampleRate = double.Parse(fields[2].Substring(1));
                    }

                    var metric_type = fields[1].Trim();
                    if (metric_type == "ms")
                    {
                        if (!timers.ContainsKey(key))
                        {
                            timers[key] = new List<long>();
                            timer_counters[key] = 0;
                        }
                        timers[key].Add(long.Parse(fields[0]));
                        timer_counters[key] += (long)(1d / sampleRate);
                    }
                    else if (metric_type == "g")
                    {
                        if (gauges.ContainsKey(key) && (fields[0].StartsWith("+") || fields[0].StartsWith("-")))
                        {
                            gauges[key] += int.Parse(fields[0]);
                        }
                        else
                        {
                            gauges[key] = int.Parse(fields[0]);
                        }
                    }
                    else if (metric_type == "s")
                    {
                        if (!sets.ContainsKey(key))
                        {
                            sets[key] = new HashSet<string>();
                        }
                        sets[key].Add(fields[0]);
                    }
                    else
                    {
                        if (!counters.ContainsKey(key))
                        {
                            counters[key] = 0;
                        }
                        counters[key] += (long)Math.Round(double.Parse(fields[0]) * (1d / sampleRate));
                    }
                }
            }
            finally
            {
                flushMetricsReaderWriterLock.ExitReadLock();
            }
        }

        private static bool IsDouble(string str)
        {
            double num = 0;
            return double.TryParse(str, out num);
        }

        private static bool IsInteger(string str)
        {
            long num = 0;
            return long.TryParse(str, out num);
        }

        private static bool IsValidSampleRate(string str)
        {
            var validSampleRate = false;
            if (str.Length > 1 && str[0] == '@')
            {
                var numberStr = str.Substring(1);
                validSampleRate = IsDouble(numberStr) && numberStr[0] != '-';
            }
            return validSampleRate;
        }

        private static bool IsValidPacket(string[] fields)
        {
            // test for existing metrics type
            if (fields == null || fields.Length < 2)
            {
                return false;
            }

            // filter out malformed sample rates
            if (fields.Length >= 3)
            {
                if (!IsValidSampleRate(fields[2]))
                {
                    return false;
                }
            }

            // filter out invalid metrics values
            switch (fields[1])
            {
                case "s":
                    return true;
                case "g":
                    return IsInteger(fields[0]);
                case "ms":
                    return IsInteger(fields[0]) && double.Parse(fields[0]) >= 0;
                default:
                    return IsInteger(fields[0]);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clearwave.Statsd
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var stats = new StatsCollector();
            var listenerPort = int.Parse(ConfigurationManager.AppSettings["statsd_port"]);
            stats.FlushInterval = int.Parse(ConfigurationManager.AppSettings["statsd_FlushInterval"]);
            stats.PctThreshold = ConfigurationManager.AppSettings["statsd_PctThreshold"].Split(',').Select(x => int.Parse(x)).ToArray();
            stats.FlushToConsole = bool.Parse(ConfigurationManager.AppSettings["statsd_FlushToConsole"]);
            stats.DeleteIdleStats = bool.Parse(ConfigurationManager.AppSettings["statsd_DeleteIdleStats"]);
            var flushToDatabase = bool.Parse(ConfigurationManager.AppSettings["statsd_FlushToDatabase"]);
            if (flushToDatabase)
            {
                stats.OnFlush += new Action<long, Metrics>((time_stamp, metrics) =>
                {
                    foreach (var item in metrics.gauges)
                    {
                        MetricsDatabase.RecordGauge(item.Key, time_stamp, item.Value);
                    }
                    foreach (var item in metrics.counters)
                    {
                        var rate = metrics.counter_rates[item.Key];
                        MetricsDatabase.RecordCounter(item.Key, time_stamp, item.Value, rate);
                    }
                    foreach (var item in metrics.sets)
                    {
                        MetricsDatabase.RecordSet(item.Key, time_stamp, item.Value.Count);
                    }
                    foreach (var item in metrics.timers)
                    {
                        MetricsDatabase.RecordTimer(item.Key, time_stamp, metrics.timer_data[item.Key]);
                    }
                });
            }
            stats.StartFlushTimer();

            Task.Run(() =>
            {
                using (var udpClient = new UdpClient(listenerPort))
                {
                    // udpClient.Client.ReceiveBufferSize initially is 8k - which should be enough for our tiny packets
                    Console.WriteLine("UDP listener started on port " + listenerPort);
                    while (true)
                    {
                        var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        var receiveBuffer = udpClient.Receive(ref remoteEndPoint);
                        try
                        {
                            var packet = Encoding.ASCII.GetString(receiveBuffer);
                            stats.Handle(packet);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Exception Handling Packet: " + e.Message);
                        }
                    }
                }
            });

#if DEBUG
            SampleSender.Start();
#endif

            new AutoResetEvent(false).WaitOne();
        }
    }
}

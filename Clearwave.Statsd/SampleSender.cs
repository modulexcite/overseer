using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clearwave.Statsd
{
    public static class SampleSender
    {
        public static void Start()
        {
            // An example of how to send to Statsd

            var cpuCounter = new PerformanceCounter();
            cpuCounter.CategoryName = "Processor";
            cpuCounter.CounterName = "% Processor Time";
            cpuCounter.InstanceName = "_Total";
            cpuCounter.NextValue();
            var ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            ramCounter.NextValue();

            var port = int.Parse(ConfigurationManager.AppSettings["statsd_port"]);
            var udpClient = new UdpClient("127.0.0.1", port);

            var stopwatch = new Stopwatch();

            var r = new Random();
            while (true)
            {
                stopwatch.Restart();
                var buffer = Encoding.ASCII.GetBytes("local.cpu:" + Math.Round(cpuCounter.NextValue()).ToString("F0") + "|g");
                var bytesSent = udpClient.Send(buffer, buffer.Length);

                buffer = Encoding.ASCII.GetBytes("local.memfree:" + ramCounter.NextValue().ToString("F0") + "|g");
                bytesSent = udpClient.Send(buffer, buffer.Length);

                buffer = Encoding.ASCII.GetBytes("local.hits:1|c");
                bytesSent = udpClient.Send(buffer, buffer.Length);

                buffer = Encoding.ASCII.GetBytes("local.random:" + r.Next(1000).ToString("F0") + "|ms");
                bytesSent = udpClient.Send(buffer, buffer.Length);

                stopwatch.Stop();

                buffer = Encoding.ASCII.GetBytes("local.sampletime:" + stopwatch.Elapsed.TotalMilliseconds.ToString("F0") + "|ms");
                bytesSent = udpClient.Send(buffer, buffer.Length);

                Thread.Sleep(500);
            }
        }
    }
}

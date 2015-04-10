﻿using System;
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
            var stats = new Stats();
            stats.ListenerPort = int.Parse(ConfigurationManager.AppSettings["statsd_port"]);
            stats.FlushInterval = int.Parse(ConfigurationManager.AppSettings["statsd_FlushInterval"]);
            stats.PctThreshold = ConfigurationManager.AppSettings["statsd_PctThreshold"].Split(',').Select(x => int.Parse(x)).ToArray();
            stats.FlushToConsole = bool.Parse(ConfigurationManager.AppSettings["statsd_FlushToConsole"]);

            var interval = new Timer(stats.FlushMetrics, null, stats.FlushInterval, stats.FlushInterval);
            Console.WriteLine("Flushing every " + stats.FlushInterval + "ms");

            Task.Run(() =>
            {
                using (var udpClient = new UdpClient(stats.ListenerPort))
                {
                    // udpClient.Client.ReceiveBufferSize initially is 8k - which should be enough for our tiny packets
                    Console.WriteLine("UDP listener started on port " + stats.ListenerPort);
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
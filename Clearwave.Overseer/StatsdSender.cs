using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Clearwave.Overseer
{
    public class StatsdSender
    {
        public static string Server = ConfigurationManager.AppSettings["statsd_server"];
        public static int Port = int.Parse(ConfigurationManager.AppSettings["statsd_port"]);

        static StatsdSender()
        {
            udpClient = new UdpClient(Server, Port);
        }

        private static readonly UdpClient udpClient;

        public static void Send(string packet)
        {
            var buffer = Encoding.ASCII.GetBytes(packet);
            var bytesSent = udpClient.Send(buffer, buffer.Length);
            Console.WriteLine("Sent " + bytesSent + " bytes");
        }
    }
}

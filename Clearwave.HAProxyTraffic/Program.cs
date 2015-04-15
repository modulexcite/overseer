using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace Clearwave.HAProxyTraffic
{
    public class Program
    {
        public static readonly ILog Log = LogManager.GetLogger("Clearwave.HAProxyTraffic");

        public static void Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();

            var listenerPort = int.Parse(ConfigurationManager.AppSettings["syslog_port"]);
            TrafficLog.Start();

            Task.Run(() =>
            {
                using (var udpClient = new UdpClient(listenerPort))
                {
                    Program.Log.Info("UDP listener started on port " + listenerPort);
                    while (true)
                    {
                        var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        var receiveBuffer = udpClient.Receive(ref remoteEndPoint);
                        try
                        {
                            var packet = Encoding.ASCII.GetString(receiveBuffer);
                            TrafficLog.QueuePacket(packet);
                        }
                        catch (Exception e)
                        {
                            Program.Log.Error("Exception Handling Packet: ", e);
                        }
                    }
                }
            });

            new AutoResetEvent(false).WaitOne();

            var samples = new[] {
                @"<165>Apr 10 08:09:20 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 70.88.217.41:61709 [10/Apr/2015:08:09:20.593] http-web~ http-web/atl-web02 50/0/0/32/82 200 354 - - ---- 932/918/4/4/0 0/0 {Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko|secure.clearwaveinc.com|} {test||||} ""GET /v2.5/ProviderPortal/VisitList/RefreshTabs?date=Fri+Apr+10+2015&fetchMultipleLocations=true&_=1428667760826 HTTP/1.1""",
                @"<165>Apr 10 08:09:20 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 72.242.47.171:55828 [10/Apr/2015:08:09:20.647] http-web~ http-web/atl-web01 27/0/0/1/28 304 114 - - ---- 932/918/3/1/0 0/0 {Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.1; WOW64; Trident/6.0)|secure.clearwaveinc.com|} {test||||} ""GET /v2.5/ProviderPortal/static/images/logout.png?r=0 HTTP/1.1""",
                @"<165>Apr 10 08:09:20 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 50.247.235.25:59167 [10/Apr/2015:08:09:10.738] http-web~ http-web/atl-web02 9907/0/0/31/9938 200 530 - - ---- 932/918/2/3/0 0/0 {Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko|secure.clearwaveinc.com|} {test||||} ""POST /v2.5/ProviderPortal/Encounters/ListItems HTTP/1.1""",
                @"<165>Apr 10 08:09:20 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 192.168.194.11:49972 [10/Apr/2015:08:09:19.399] http-applicationservicehost http-applicationservicehost/atl-app01 1271/0/1/5/1277 200 859 - - ---- 932/12/0/1/0 0/0 {|cw-l-edi|} {||||} ""POST /EdiServiceServiceHost/EligibilityProviderService.svc HTTP/1.1""",
                @"<165>Apr 10 08:09:20 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 192.168.194.11:49972 [10/Apr/2015:08:09:20.676] http-applicationservicehost http-applicationservicehost/atl-app02 1/0/1/7/9 200 1283 - - ---- 932/12/0/1/0 0/0 {|cw-l-edi|} {||||} ""POST /EdiServiceServiceHost/ReportingService.svc HTTP/1.1""",
                @"<165>Apr 10 08:09:20 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 216.52.192.177:55956 [10/Apr/2015:08:09:11.185] http-web~ http-web/atl-web02 9452/0/0/51/9503 200 382 - - ---- 932/918/1/2/0 0/0 {Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.1; WOW64; Trident/6.0)|secure.clearwaveinc.com|} {test||||} ""GET /v2.5/ProviderPortal/PortalMessages/PortalMessages?_=1428667760655 HTTP/1.1""",
                @"<165>Apr 10 13:40:03 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 66.18.123.30:60977 [10/Apr/2015:13:39:53.133] http-web~ http-web/atl-web01 10031/0/1/25/10057 200 354 - - ---- 2035/2013/16/9/0 0/0 {Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; WOW64; Trident/5.0)|secure.clearwaveinc.com|} {test||||} ""GET /v2.5/ProviderPortal/VisitList/RefreshTabs?date=Fri+Apr+10+2015&fetchMultipleLocations=false&_=1428687603469 HTTP/1.1""",
                @"<165>Apr 10 13:40:03 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 208.83.94.218:31385 [10/Apr/2015:13:40:03.129] http-web~ http-web/atl-web01 49/0/0/17/66 200 354 - - ---- 2035/2013/15/8/0 0/0 {Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko|secure.clearwaveinc.com|} {||||} ""GET /v2.5/ProviderPortal/VisitList/RefreshTabs?date=Fri+Apr+10+2015&fetchMultipleLocations=false&_=1428687602502 HTTP/1.1""",
                @"<165>Apr 10 13:40:03 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 216.52.192.177:65503 [10/Apr/2015:13:39:53.202] http-web~ http-web/atl-web02 9969/0/1/27/9997 200 1143 - - ---- 2035/2013/16/9/0 0/0 {Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.1; WOW64; Trident/6.0)|secure.clearwaveinc.com|} {||||} ""POST /v2.5/ProviderPortal/Encounters/ListItems HTTP/1.1""",
                @"<165>Apr 10 13:40:03 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 192.168.194.10:62905 [10/Apr/2015:13:40:03.102] http-applicationservicehost http-applicationservicehost/atl-app01 96/0/0/3/99 200 925 - - ---- 2035/19/2/2/0 0/0 {|cw-l-edi|} {||||} ""POST /EdiServiceServiceHost/EligibilityProviderService.svc HTTP/1.1""",
                @"<165>Apr 10 13:40:03 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 192.168.194.10:62905 [10/Apr/2015:13:40:03.201] http-applicationservicehost http-applicationservicehost/atl-app02 1/0/1/4/6 200 4012 - - ---- 2035/19/2/2/0 0/0 {|cw-l-edi|} {||||} ""POST /EdiServiceServiceHost/ReportingService.svc HTTP/1.1""",
                @"<165>Apr 10 13:40:03 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 66.162.112.42:40596 [10/Apr/2015:13:40:03.048] http-web~ http-web/atl-web01 116/0/0/47/163 200 383 - - ---- 2035/2013/17/9/0 0/0 {Mozilla/5.0 (Windows NT 6.1; WOW64; rv:35.0) Gecko/20100101 Firefox/35.0|secure.clearwaveinc.com|} {||||} ""GET /v2.5/ProviderPortal/PortalMessages/PortalMessages?_=1428687602946 HTTP/1.1""",
                @"<134>Apr 10 21:52:30 atl-lb01.prod.clearwaveinc.com haproxy[2041]: 199.227.242.46:62615 [10/Apr/2015:21:52:20.343] http-web~ http-web/atl-web02 9961/0/0/176/10137 200 1226 - - ---- 49/43/0/1/0 0/0 {Mozilla/5.0 (Windows NT 6.1; WOW64; chromeframe/29.0.1547.76) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/29.0.1547.76 Safari/|secure.clearwaveinc.com|} {||||} ""POST /v2.5/ProviderPortal/Encounters/ListItems HTTP/1.1""",
            };

            var r = new Random();

            while (true)
            {
                foreach (var s in samples)
                {
                    TrafficLog.QueuePacket(s);
                    Thread.Sleep((int)Math.Round((r.Next(100) / 160d) + 1) - 1);
                }
            }
        }
    }
}

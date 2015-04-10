using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Clearwave.HAProxyTraffic
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var packet = @"<165>Apr 10 08:09:20 ATL-LB01 atl-lb01.prod.clearwaveinc.com haproxy[13927]: 70.88.217.41:61709 [10/Apr/2015:08:09:20.593] http-web~ http-web/atl-web02 50/0/0/32/82 200 354 - - ---- 932/918/4/4/0 0/0 {https://secure.clearwaveinc.com/v2.5/ProviderPortal/VisitList/Al|Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko|secure.clearwaveinc.com||gzip, deflate} {gzip} ""GET /v2.5/ProviderPortal/VisitList/RefreshTabs?date=Fri+Apr+10+2015&fetchMultipleLocations=true&_=1428667760826 HTTP/1.1""";
            var endpoint = "localhost";

            var msg = SyslogMessage.Parse(packet, endpoint);
            var ha = haproxyRegex.Match(msg.Message);
            for (int i = 0; i < ha.Groups.Count; i++)
            {
                Console.WriteLine("Group " + i + " = " + ha.Groups[i].Value);
            }
        }


        // Field   Format                                Extract from the example above
        // 1   process_name '[' pid ']:'                            haproxy[14389]:
        // 2   client_ip ':' client_port                             10.0.1.2:33317
        // 3   '[' accept_date ']'                       [06/Feb/2009:12:14:14.655]
        // 4   frontend_name                                                http-in
        // 5   backend_name '/' server_name                             static/srv1
        // 6   Tq '/' Tw '/' Tc '/' Tr '/' Tt*                       10/0/30/69/109
        // 7   status_code                                                      200
        // 8   bytes_read*                                                     2750
        // 9   captured_request_cookie                                            -
        // 10   captured_response_cookie                                           -
        // 11   termination_state                                               ----
        // 12   actconn '/' feconn '/' beconn '/' srv_conn '/' retries*    1/1/1/1/0
        // 13   srv_queue '/' backend_queue                                      0/0
        // 14   '{' captured_request_headers* '}'                   {haproxy.1wt.eu}
        // 15   '{' captured_response_headers* '}'                                {}
        // 16   '"' http_request '"'                      "GET /index.html HTTP/1.1"


        // Group 1 = atl-lb01.prod.clearwaveinc.com
        // Group 2 = haproxy
        // Group 3 = 13927
        // Group 4 = 70.88.217.41
        // Group 5 = 61709
        // Group 6 = 10/Apr/2015:08:09:20.593
        // Group 7 = http-web~
        // Group 8 = http-web
        // Group 9 = atl-web02
        // Group 10 = 50/0/0/32/82   = Tq '/' Tw '/' Tc '/' Tr '/' Tt*
        // Group 11 = 200  status_code
        // Group 12 = 354  bytes_read
        // Group 13 = -
        // Group 14 = -
        // Group 15 = ---- = termination_state
        // Group 16 = 932/918/4/4/0 = actconn '/' feconn '/' beconn '/' srv_conn '/' retries*
        // Group 17 = 0/0 = srv_queue '/' backend_queue
        // Group 18 = https://secure.clearwaveinc.com/v2.5/ProviderPortal/VisitList/Al|lla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko|secure.clearwaveinc.com||gzip, deflate
        // Group 19 = gzip
        // Group 20 = GET
        // Group 21 = /v2.5/ProviderPortal/VisitList/RefreshTabs?date=Fri+Apr+10+2015&fMultipleLocations=true&_=1428667760826
        // Group 22 = HTTP/1.1


        private static Regex haproxyRegex =
            new Regex(
                @"^(\S+) (\S+)\[(\d+)\]: (\S+):(\d+) \[(\S+)\] (\S+) (\S+)\/(\S+) (\S+) (\S+) (\S+) *(\S+) (\S+) (\S+) (\S+) (\S+) \{([^}]*)\} \{([^}]*)\} ""(\S+) ([^""]+) (\S+)"".*$"
                , RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}

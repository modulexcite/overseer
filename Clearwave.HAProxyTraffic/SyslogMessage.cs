using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Clearwave.HAProxyTraffic
{
    [Serializable]
    public class SyslogMessage
    {
        public SyslogMessage(int priority, DateTime timestamp, string hostname, string message)
        {
            if (priority > 0)
            {
                // The facility code is the nearest whole number of the priority value divided by 8
                Facility = (FacilityCode)(int)Math.Floor((double)priority / 8);
                // The severity code is the remainder of the priority value divided by 8
                Severity = (SeverityCode)(priority % 8);
            }
            else
            {
                Facility = FacilityCode.None;
                Severity = SeverityCode.None;
            }

            Timestamp = timestamp;
            Hostname = hostname;
            Message = message;
        }

        public FacilityCode Facility { get; private set; }
        public SeverityCode Severity { get; private set; }
        public DateTime Timestamp { get; private set; }
        public string Hostname { get; private set; }
        public string Message { get; private set; }

        private static Regex msgRegex = new Regex(@"
(\<(?<PRI>\d{1,3})\>){0,1}
(?<HDR>
  (?<TIMESTAMP>
    (?<MMM>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s
    (?<DD>[ 0-9][0-9])\s
    (?<HH>[0-9]{2})\:(?<MM>[0-9]{2})\:(?<SS>[0-9]{2})
  )\s
  (?<HOSTNAME>
    [^ ]+?
  )\s
){0,1}
(?<MSG>.*)
", RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

        public static SyslogMessage Parse(string packet, string endpoint)
        {
            var m = msgRegex.Match(packet);
            //If a match is not found the message is not valid
            if (m != null && !string.IsNullOrEmpty(packet))
            {
                //parse PRI section into a priority value
                int priority = 0;
                int.TryParse(m.Groups["PRI"].Value, out priority);

                //parse the HEADER section - contains TIMESTAMP and HOSTNAME
                string hostname = null;
                DateTime timestamp = DateTime.Now;

                // Get the timestamp and hostname from the header of the message
                if (!string.IsNullOrEmpty(m.Groups["HDR"].Value))
                {
                    if (!string.IsNullOrEmpty(m.Groups["TIMESTAMP"].Value))
                    {
                        DateTime.TryParseExact(m.Groups["TIMESTAMP"].Value, "MMM dd HH:mm:ss", DateTimeFormatInfo.InvariantInfo, DateTimeStyles.None, out timestamp);
                    }
                    if (!string.IsNullOrEmpty(m.Groups["HOSTNAME"].Value))
                    {
                        hostname = m.Groups["HOSTNAME"].Value;
                    }
                }

                if (string.IsNullOrEmpty(hostname))
                {
                    hostname = endpoint.ToString();
                }

                string message = "";
                if ((m.Groups["MSG"].Value) != null)
                {
                    message = m.Groups["MSG"].Value;
                }
                return new SyslogMessage(priority, timestamp, hostname, message);
            }
            return null;
        }
    }
}

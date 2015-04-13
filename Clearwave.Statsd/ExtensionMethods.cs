using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clearwave.Statsd
{
    public static class ExtensionMethods
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
        public static double DateTimeToUnixTimestamp(this DateTime dateTime)
        {
            return (dateTime - Epoch.ToLocalTime()).TotalSeconds;
        }
        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            return Epoch.AddSeconds(unixTimeStamp).ToLocalTime();
        }
    }
}

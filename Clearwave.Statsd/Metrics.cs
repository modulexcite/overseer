using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clearwave.Statsd
{
    public class Metrics
    {
        public Dictionary<string, long> counters { get; set; }
        public Dictionary<string, double> counter_rates { get; set; }

        public Dictionary<string, long> gauges { get; set; }

        public Dictionary<string, List<long>> timers { get; set; }
        public Dictionary<string, long> timer_counters { get; set; }
        public Dictionary<string, Dictionary<string, long>> timer_data { get; set; }

        public Dictionary<string, HashSet<string>> sets { get; set; }

        public int[] pctThreshold { get; set; }
        public Dictionary<string, long> statsd_metrics { get; set; }
    }
}

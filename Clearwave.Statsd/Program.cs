using System;
using System.Collections.Generic;
using System.Linq;
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
            stats.Start();

            new AutoResetEvent(false).WaitOne();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Clearwave.Overseer.vSphere
{
    public class QuickStatsPoller
    {
        public QuickStatsPoller(ManagementAPI api, Action<string> sendToStats)
        {
            this.api = api;
            this.sendToStats = sendToStats;
        }

        private readonly ManagementAPI api;
        private readonly Action<string> sendToStats;

        private Timer interval;

        public void Start(int intervalInMS)
        {
            if (interval != null)
            {
                throw new InvalidOperationException("Polling Already Started!");
            }
            interval = new Timer(HandleTimerInterval, null, 0, intervalInMS);
        }

        private void HandleTimerInterval(object state)
        {
            var hosts = api.RetrievePropertiesForAllObjectsOfType("HostSystem", properties: new[] { 
                "name", // cw-vm01.becacorp.com
                "summary.hardware.cpuMhz", // 2593
                "summary.hardware.memorySize", // 309201162240 bytes
                "summary.hardware.numCpuCores", // 16
                "summary.quickStats.overallCpuUsage",    // 5956 MHz
                "summary.quickStats.overallMemoryUsage", // 72669 MB
                //"summary.hardware.otherIdentifyingInfo", // "xml string"
                //"summary.hardware.model", // ProLiant DL380p Gen8
            });

            var host = hosts.First().Value;
            var host_name = host["name"];
            if (host_name.Contains(".")) { host_name = host_name.Substring(0, host_name.IndexOf(".")); }
            var host_cpuMhz = double.Parse(host["summary.hardware.cpuMhz"]);
            var host_numCpuCores = double.Parse(host["summary.hardware.numCpuCores"]);
            var host_overallCpuUsage = double.Parse(host["summary.quickStats.overallCpuUsage"]);
            var host_memorySize = double.Parse(host["summary.hardware.memorySize"]) / 1024 / 1024;
            var host_overallMemoryUsage = double.Parse(host["summary.quickStats.overallMemoryUsage"]);

            var host_pcCPU = (host_overallCpuUsage / (host_cpuMhz * host_numCpuCores)) * 100;
            var host_pcMem = (host_overallMemoryUsage / host_memorySize) * 100;
            sendToStats("vmware." + host_name + ".cpu:" + host_pcCPU.ToString("F0") + "|g");
            sendToStats("vmware." + host_name + ".memory:" + host_pcMem.ToString("F0") + "|g");

            var virtualMachines = api.RetrievePropertiesForAllObjectsOfType("VirtualMachine", properties: new[] { 
                "name", // ATL-FS01
                //"runtime.host", // ha-host
                //"guest.guestFullName", // Microsoft Windows Server 2012 (64-bit)
                //"guest.hostName", // ATL-FS01.prod.clearwaveinc.com
                //"guest.ipAddress", // 169.254.2.249
                //"guest.guestState", // running
                //"guest.disk", // XML array
                "config.hardware.memoryMB", // 8192
                "config.hardware.numCPU", // 2
                "runtime.maxCpuUsage", // 5186 (i.e. 2 x 2593)
                "runtime.maxMemoryUsage", // 8192
                "summary.quickStats.balloonedMemory", // 0
                "summary.quickStats.guestMemoryUsage", // 491
                "summary.quickStats.hostMemoryUsage", // 8256
                "summary.quickStats.overallCpuUsage", // 208
                "summary.quickStats.uptimeSeconds",  // 19816104
            });

            foreach (var vm in virtualMachines.Values)
            {
                var vm_name = vm["name"];
                var vm_maxCpuUsage = double.Parse(vm["runtime.maxCpuUsage"]);
                var vm_maxMemoryUsage = double.Parse(vm["runtime.maxMemoryUsage"]);
                var vm_overallCpuUsage = double.Parse(vm["summary.quickStats.overallCpuUsage"]);
                var vm_guestMemoryUsage = double.Parse(vm["summary.quickStats.guestMemoryUsage"]);
                var vm_uptimeSeconds = long.Parse(vm["summary.quickStats.uptimeSeconds"]);

                var pcCPU = (vm_overallCpuUsage / vm_maxCpuUsage) * 100;
                var pcMem = (vm_guestMemoryUsage / vm_maxMemoryUsage) * 100;

                sendToStats("vmware." + host_name + "." + vm_name + ".cpu:" + pcCPU.ToString("F0") + "|g");
                sendToStats("vmware." + host_name + "." + vm_name + ".memory:" + pcMem.ToString("F0") + "|g");
                sendToStats("vmware." + host_name + "." + vm_name + ".uptime:" + vm_uptimeSeconds.ToString("F0") + "|g");
            }
        }
    }
}

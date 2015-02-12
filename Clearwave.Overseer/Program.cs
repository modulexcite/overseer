using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Clearwave.Overseer.vSphere;

namespace Clearwave.Overseer
{
    class Program
    {
        static void Main(string[] args)
        {
            var sc = new ServerConnection(@"https://127.0.0.1/sdk");
            var sm = new SessionManager(sc, "username", "password");

            sm.ConnectAndLogin();



            var props = sm.RetrievePropertiesForAllObjectsOfType("HostSystem", properties: new[] { 
                "name",
		"summary.hardware.cpuMhz",
		"summary.hardware.memorySize", // bytes
		"summary.hardware.numCpuCores",
		"summary.hardware.numCpuCores",
		"summary.quickStats.overallCpuUsage",    // MHz
		"summary.quickStats.overallMemoryUsage", // MB
		"summary.hardware.otherIdentifyingInfo",
		"summary.hardware.model",});
            Console.WriteLine("HostSystem");
            foreach (var item in props.Keys)
            {
                foreach (var prop in props[item].Keys)
                {
                    Console.WriteLine(item + " | " + prop + "=" + props[item][prop]);
                }
                Console.WriteLine("--");
                Console.WriteLine("");
            }
            Console.ReadLine();

            props = sm.RetrievePropertiesForAllObjectsOfType("VirtualMachine", properties: new[] { 
                "name",
                "runtime.host",
                "guest.guestFullName",
                "guest.hostName",
                "guest.ipAddress",
                "guest.guestState",
                "guest.disk",
                "config.hardware.memoryMB",
                "config.hardware.numCPU",
                "summary.quickStats.balloonedMemory",
                "summary.quickStats.guestMemoryUsage",
                "summary.quickStats.hostMemoryUsage",
                "summary.quickStats.overallCpuUsage",
                "summary.quickStats.uptimeSeconds", });
            Console.WriteLine("VirtualMachine");
            foreach (var item in props.Keys)
            {
                foreach (var prop in props[item].Keys)
                {
                    Console.WriteLine(item + " | " + prop + "=" + props[item][prop]);
                }
                Console.WriteLine("--");
                Console.WriteLine("");
            }

            Console.ReadLine();

             props = sm.RetrievePropertiesForAllObjectsOfType("Datastore", properties: new[] { 
                "name",
		"summary.capacity",
		"summary.freeSpace", }, rootFolderName: "ha-folder-datastore");
             Console.WriteLine("Datastore");
             foreach (var item in props.Keys)
             {
                 foreach (var prop in props[item].Keys)
                 {
                     Console.WriteLine(item + " | " + prop + "=" + props[item][prop]);
                 }
                 Console.WriteLine("--");
                 Console.WriteLine("");
             }

             Console.ReadLine();
        }
    }
}


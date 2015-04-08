using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Clearwave.Overseer.WatchGuard
{
    public class VPNStatusXml
    {
        public static List<VPNStatusXmlGateway> ParseStatistics(string xml)
        {
            var statusXml = XDocument.Parse(xml).Root;
            var gatewayList = statusXml.XPathSelectElement("/status/cluster/aggregate/ike/gateway/list");
            var tunnelList = statusXml.XPathSelectElement("/status/cluster/aggregate/ipsec/sa/list");
            var counterStats = statusXml.XPathSelectElement("/status/ike/counters");

            var gateways = new List<Clearwave.Overseer.WatchGuard.VPNStatusXmlGateway>();
            foreach (var item in gatewayList.Elements("gateway"))
            {
                gateways.Add(new WatchGuard.VPNStatusXmlGateway()
                {
                    Name = item.Element("name").Value,
                    Enabled = item.Element("enabled").Value == "1",
                    IKEPolicyList = item.Element("ike_policy_list").Elements("ike_policy").Select(x => x.Value).ToArray(),
                });
            }
            var gatewayDictionary = gateways.ToDictionary(x => x.Name, StringComparer.InvariantCultureIgnoreCase);
            foreach (var item in tunnelList.Elements("sa_brief"))
            {
                var gateway = gatewayDictionary[item.Element("ike_policy").Value];
                var inbound = item.Element("dir").Value == "0";
                if (inbound)
                {
                    var remoteNetwork = item.Element("selector").Element("local_start").Value; // other way around
                    var localNetwork = item.Element("selector").Element("remote_start").Value; // other way around
                    var remoteGateway = item.Element("source").Value;

                    var tunnel = gateway.Tunnels.SingleOrDefault(x => x.RemoteNetwork == remoteNetwork && x.LocalNetwork == localNetwork);
                    if (tunnel == null)
                    {
                        tunnel = new WatchGuard.VPNStatusXmlTunnel();
                        tunnel.IKEPolicy = item.Element("ike_policy").Value;
                        tunnel.IPSECPolicy = item.Element("ipsec_policy").Value;
                        tunnel.RemoteGateway = remoteGateway;
                        tunnel.LocalNetwork = localNetwork;
                        tunnel.RemoteNetwork = remoteNetwork;
                        gateway.Tunnels.Add(tunnel);
                    }

                    tunnel.CreatedTime = Stats.UnixTimeStampToDateTime(double.Parse(item.Element("created_time").Value));
                    tunnel.received_total_nbytes = long.Parse(item.Element("total_nbytes").Value);
                    tunnel.received_total_npkts = long.Parse(item.Element("total_npkts").Value);
                    tunnel.total_rekeys = int.Parse(item.Element("total_rekeys").Value);
                }
                else
                {
                    var localNetwork = item.Element("selector").Element("local_start").Value;
                    var remoteNetwork = item.Element("selector").Element("remote_start").Value;
                    var remoteGateway = item.Element("destination").Value; // other way around

                    var tunnel = gateway.Tunnels.SingleOrDefault(x => x.RemoteNetwork == remoteNetwork && x.LocalNetwork == localNetwork);
                    if (tunnel == null)
                    {
                        tunnel = new WatchGuard.VPNStatusXmlTunnel();
                        tunnel.IKEPolicy = item.Element("ike_policy").Value;
                        tunnel.IPSECPolicy = item.Element("ipsec_policy").Value;
                        tunnel.RemoteGateway = remoteGateway;
                        tunnel.LocalNetwork = localNetwork;
                        tunnel.RemoteNetwork = remoteNetwork;
                        gateway.Tunnels.Add(tunnel);
                    }

                    tunnel.CreatedTime = Stats.UnixTimeStampToDateTime(double.Parse(item.Element("created_time").Value));
                    tunnel.sent_total_nbytes = long.Parse(item.Element("total_nbytes").Value);
                    tunnel.sent_total_npkts = long.Parse(item.Element("total_npkts").Value);
                    tunnel.total_rekeys = int.Parse(item.Element("total_rekeys").Value);
                }
            }

            return gateways;
        }
    }

    public class VPNStatusXmlGateway
    {
        public VPNStatusXmlGateway()
        {
            Tunnels = new List<VPNStatusXmlTunnel>();
        }

        public string Name { get; set; }
        public string IKEPolicy { get { return IKEPolicyList[0]; } set { IKEPolicyList = new[] { value }; } }
        public string[] IKEPolicyList { get; set; }
        public bool Enabled { get; set; }

        public List<VPNStatusXmlTunnel> Tunnels { get; set; }
    }

    public class VPNStatusXmlTunnel
    {
        public string IKEPolicy { get; set; }
        public string IPSECPolicy { get; set; }

        public string RemoteGateway { get; set; }

        public string LocalNetwork { get; set; }
        public string RemoteNetwork { get; set; }

        public DateTime CreatedTime { get; set; }

        public long received_total_nbytes { get; set; } // dir = 0
        public long received_total_npkts { get; set; } // dir = 0

        public long sent_total_nbytes { get; set; } // dir = 1
        public long sent_total_npkts { get; set; } // dir = 1
        
        public int total_rekeys { get; set; }
    }
}
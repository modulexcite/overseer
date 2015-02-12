using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Clearwave.Overseer.vSphere
{
    public class ManagementAPI
    {
        private readonly ServerConnection serverConnection;
        private readonly string username;
        private readonly string password;

        public ManagementAPI(ServerConnection serverConnection, string username, string password)
        {
            this.serverConnection = serverConnection;
            this.username = username;
            this.password = password;
        }

        public void ConnectAndLogin()
        {
            Connected = false;
            containerViews.Clear();
            serverConnection.ClearSessionCookie();
            var retrieveServiceContentResponse = ExecuteSOAPMethod(Method_RetrieveServiceContent);
            ParseServiceContent(retrieveServiceContentResponse);
            var loginResponse = ExecuteSOAPMethod(string.Format(Method_Login, username, password, SessionManagerName));
            ParseLogin(loginResponse);
            Connected = true;
        }

        public bool Connected { get; private set; }
        public string RootFolderName { get; private set; }
        public string SessionManagerName { get; private set; }
        public string PropertyCollectorName { get; private set; }
        public string ViewManagerName { get; private set; }
        public string VMWareVersion { get; private set; }
        public string APIVersion { get; private set; }
        public string LoginKey { get; private set; }
        public string LoginFullName { get; private set; }
        private readonly Dictionary<string, string> containerViews = new Dictionary<string, string>();

        private void ParseServiceContent(XElement serviceContent)
        {
            serviceContent = serviceContent.Element("returnval");
            RootFolderName = serviceContent.Element("rootFolder").Value;
            SessionManagerName = serviceContent.Element("sessionManager").Value;
            PropertyCollectorName = serviceContent.Element("propertyCollector").Value;
            ViewManagerName = serviceContent.Element("viewManager").Value;
            VMWareVersion = serviceContent.Element("about").Element("fullName").Value;
            APIVersion = serviceContent.Element("about").Element("apiVersion").Value;
        }

        private void ParseLogin(XElement loginResponse)
        {
            loginResponse = loginResponse.Element("returnval");
            LoginKey = loginResponse.Element("key").Value;
            LoginFullName = loginResponse.Element("fullName").Value;
        }

        public XElement ExecuteSOAPMethod(string body)
        {
            var xmlString = serverConnection.ExecutPostMethod(WrapInSOAPEnvelope(body));
            return ParseSOAPResponseAndReturnBodyInner(xmlString);
        }

        public Dictionary<string, Dictionary<string, string>> RetrievePropertiesForAllObjectsOfType(string managedObjectType, string[] properties, string rootFolderName = null)
        {
            var pathSet = @"<all>true</all>";
            if (properties != null && properties.Any())
            {
                pathSet = string.Join("", properties.Select(x => string.Format("<pathSet>{0}</pathSet>", x)));
            }
            var sessionId = GetOrCreateContainerView(managedObjectType, rootFolderName ?? RootFolderName);
            var body = string.Format(Method_RetrieveProperties, PropertyCollectorName, managedObjectType, pathSet, sessionId);
            var propsXml = ExecuteSOAPMethod(body);

            var results = new Dictionary<string, Dictionary<string,string>>();
            foreach (var item in propsXml.Elements(XName.Get("returnval")))
            {
                var managedObjectId = item.Element("obj").Value;
                var props = new Dictionary<string, string>();
                foreach (var prop in item.Elements(XName.Get("propSet")))
                {
                    var name = prop.Element("name").Value;
                    var val = prop.Element("val").HasElements ? prop.Element("val").ToString() : prop.Element("val").Value;
                    props.Add(name, val);
                }
                results.Add(managedObjectId, props);
            }
            return results;
        }

        private string GetOrCreateContainerView(string managedObjectType, string rootFolderName )
        {
            var key = rootFolderName + "|" + managedObjectType;
            if (containerViews.ContainsKey(key))
            {
                return containerViews[key];
            }
            var body = string.Format(Method_CreateContainerView, ViewManagerName, rootFolderName, managedObjectType);
            var containerViewResponse = ExecuteSOAPMethod(body);
            var sessionid = containerViewResponse.Element("returnval").Value;
            containerViews.Add(key, sessionid);
            return sessionid;
        }

        public static XElement ParseSOAPResponseAndReturnBodyInner(string xmlString)
        {
            var envelope = XDocument.Parse(xmlString.Replace("xmlns=\"urn:vim25\"", "")).Root;
            return envelope.Elements().First(x => x.Name.LocalName == "Body").Elements().First();
        }

        public static string WrapInSOAPEnvelope(string body)
        {
            return SOAPEnvelopePre + body + SOAPEnvelopePost;
        }

        public const string SOAPEnvelopePre = @"<?xml version=""1.0"" encoding=""UTF-8""?><soapenv:Envelope xmlns:soapenc=""http://schemas.xmlsoap.org/soap/encoding/"" xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""><soapenv:Body>";
        public const string SOAPEnvelopePost = @"</soapenv:Body></soapenv:Envelope>";

        public const string Method_RetrieveServiceContent = @"<RetrieveServiceContent xmlns=""urn:vim25""><_this type=""ServiceInstance"">ServiceInstance</_this></RetrieveServiceContent>";
        public const string Method_Login = @"<Login xmlns=""urn:vim25""><_this type=""SessionManager"">{2}</_this><userName>{0}</userName><password>{1}</password></Login>";

        public const string Method_CreateContainerView = @"<CreateContainerView xmlns=""urn:vim25""><_this type=""ViewManager"">{0}</_this><container type=""Folder"">{1}</container><type>{2}</type><recursive>true</recursive></CreateContainerView>";
        public const string Method_RetrieveProperties = @"<RetrieveProperties xmlns=""urn:vim25""><_this type=""PropertyCollector"">{0}</_this><specSet><propSet><type>{1}</type>{2}</propSet><objectSet><obj type=""ContainerView"">{3}</obj><skip>true</skip><selectSet xsi:type=""TraversalSpec""><name>traverseEntities</name><type>ContainerView</type><path>view</path><skip>false</skip></selectSet></objectSet></specSet></RetrieveProperties>";

    }
}

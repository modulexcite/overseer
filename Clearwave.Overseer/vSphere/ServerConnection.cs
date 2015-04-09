using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace Clearwave.Overseer.vSphere
{
    public class ServerConnection
    {
        public const int DefaultTimeout = (100 * 1000);
        private const string SessionCookieName = "vmware_soap_session";

        public ServerConnection(string address, int timeout = DefaultTimeout)
        {
            this.address = address;
            this.timeout = timeout;
            this.baseAddress = new Uri(address);
        }

        private readonly string address;
        private readonly int timeout;
        private readonly Uri baseAddress;

        public Cookie SessionCookie { get; private set; }

        public void ClearSessionCookie()
        {
            SessionCookie = null;
        }

        public static bool HandleCert(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public string ExecutPostMethod(string postData)
        {
            var request = (HttpWebRequest)HttpWebRequest.Create(baseAddress);
            request.Method = "POST";
            request.Timeout = this.timeout;
            request.ContentType = "text/xml; charset=UTF-8";
            request.UserAgent = "Clearwave.Overseer";
            request.CookieContainer = new CookieContainer();
            request.ServerCertificateValidationCallback = HandleCert;
            if (SessionCookie != null)
            {
                request.CookieContainer.Add(SessionCookie);
            }
            var postDataBuffer = Encoding.UTF8.GetBytes(postData);
            request.ContentLength = postDataBuffer.Length;
            using (var dataStream = request.GetRequestStream())
            {
                dataStream.Write(postDataBuffer, 0, postDataBuffer.Length);
            }
            var response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception(response.StatusCode.ToString() + " " + ReadContentFromResult(response));
            }
            var cookies = response.Cookies;
            if (cookies[SessionCookieName] != null)
            {
                this.SessionCookie = cookies[SessionCookieName];
            }
            var resultString = ReadContentFromResult(response);
            return resultString;
        }

        private static string ReadContentFromResult(HttpWebResponse response)
        {
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return reader.ReadToEnd();
            }
        }
    }
}

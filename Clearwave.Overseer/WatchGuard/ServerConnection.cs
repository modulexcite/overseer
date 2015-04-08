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

namespace Clearwave.Overseer.WatchGuard
{
    public class ServerConnection
    {
        public const int DefaultTimeout = (100 * 1000);
        public const string SessionCookieName = "session_id";

        public ServerConnection(int timeout = DefaultTimeout)
        {
            this.timeout = timeout;
        }

        private readonly int timeout;
        private readonly Stopwatch stopwatch = new Stopwatch();

        public Cookie SessionCookie { get; set; }

        public void ClearSessionCookie()
        {
            SessionCookie = null;
        }

        public static bool HandleCert(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public bool IsLoggedIn(string hostAndPort)
        {
            var request = (HttpWebRequest)HttpWebRequest.Create("https://" + hostAndPort + "/auth/login");
            request.Method = "GET";
            request.Timeout = this.timeout;
            request.ContentType = "text/xml; charset=UTF-8";
            request.UserAgent = "Clearwave.Overseer";
            request.CookieContainer = new CookieContainer();
            request.ServerCertificateValidationCallback = HandleCert;
            request.ServicePoint.Expect100Continue = false; // UGH
            request.AllowAutoRedirect = false;
            if (SessionCookie != null)
            {
                request.CookieContainer.Add(SessionCookie);
            }
            var response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode != HttpStatusCode.SeeOther)
            {
                return false;
            }
            return !response.Headers["Location"].Contains("login");
        }

        public bool Login(string hostAndPort, string username, string password)
        {
            stopwatch.Stop();
            stopwatch.Reset();
            stopwatch.Start();
            try
            {
                var request = (HttpWebRequest)HttpWebRequest.Create("https://" + hostAndPort + "/auth/login");
                request.Method = "POST";
                request.Timeout = this.timeout;
                request.ContentType = "application/x-www-form-urlencoded";
                request.Accept = "*/*";
                request.UserAgent = "Clearwave.Overseer";
                request.CookieContainer = new CookieContainer();
                request.ServerCertificateValidationCallback = HandleCert;
                request.ServicePoint.Expect100Continue = false; // UGH
                request.AllowAutoRedirect = false;
                var postData = string.Format("username={0}&password={1}&domain=Firebox-DB&sid={2}&privilege=1&from_page=", username, password, GetLoginSID(hostAndPort, username, password));
                var postDataBuffer = Encoding.ASCII.GetBytes(postData);
                request.ContentLength = postDataBuffer.Length;
                using (var dataStream = request.GetRequestStream())
                {
                    dataStream.Write(postDataBuffer, 0, postDataBuffer.Length);
                }
                var response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.SeeOther)
                {
                    throw new Exception(response.StatusCode.ToString() + " " + ReadContentFromResult(response));
                }
                var cookies = response.Cookies;
                if (cookies[SessionCookieName] != null)
                {
                    this.SessionCookie = cookies[SessionCookieName];
                }
                var resultString = ReadContentFromResult(response);
#if DEBUG
                Debug.WriteLine("Result: " + resultString);
#endif
                return true;
            }
            catch (WebException e)
            {
                var error = ReadContentFromResult((HttpWebResponse)e.Response);
                return false;
            }
            finally
            {
                stopwatch.Stop();
                Trace.WriteLine(string.Format("Executed GET {0}ms", stopwatch.Elapsed.TotalMilliseconds));
            }
        }

        private string GetLoginSID(string hostAndPort, string username, string password)
        {
            var request = (HttpWebRequest)HttpWebRequest.Create("https://" + hostAndPort + "/agent/login");
            request.Method = "POST";
            request.Timeout = this.timeout;
            request.ContentType = "text/xml";
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Accept = "*/*";
            request.UserAgent = "Clearwave.Overseer";
            request.CookieContainer = new CookieContainer();
            request.ServerCertificateValidationCallback = HandleCert;
            request.ServicePoint.Expect100Continue = false; // UGH
            request.AllowAutoRedirect = false;
            var postData = string.Format("<methodCall><methodName>login</methodName><params><param><value><struct><member><name>password</name><value><string>{1}</string></value></member><member><name>user</name><value><string>{0}</string></value></member><member><name>domain</name><value><string>Firebox-DB</string></value></member><member><name>uitype</name><value><string>2</string></value></member></struct></value></param></params></methodCall>", username, password);
            var postDataBuffer = Encoding.ASCII.GetBytes(postData);
            request.ContentLength = postDataBuffer.Length;
            using (var dataStream = request.GetRequestStream())
            {
                dataStream.Write(postDataBuffer, 0, postDataBuffer.Length);
            }
            var response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.SeeOther)
            {
                throw new Exception(response.StatusCode.ToString() + " " + ReadContentFromResult(response));
            }
            var cookies = response.Cookies;
            if (cookies[SessionCookieName] != null)
            {
                this.SessionCookie = cookies[SessionCookieName];
            }
            var resultString = ReadContentFromResult(response);

            resultString = resultString.Substring(resultString.IndexOf("sid") + 3);
            resultString = resultString.Substring(resultString.IndexOf("<value>") + "<value>".Length);
            resultString = resultString.Substring(0, "6282C27C16411834162EAA2F46C2E2F300000A14".Length);

            return resultString;
        }

        public string Get(string url)
        {
            stopwatch.Stop();
            stopwatch.Reset();
            stopwatch.Start();
            try
            {
                var request = (HttpWebRequest)HttpWebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = this.timeout;
                request.ContentType = "text/xml; charset=UTF-8";
                request.UserAgent = "Clearwave.Overseer";
                request.CookieContainer = new CookieContainer();
                request.ServerCertificateValidationCallback = HandleCert;
                request.ServicePoint.Expect100Continue = false; // UGH
                request.AllowAutoRedirect = false;
                if (SessionCookie != null)
                {
                    request.CookieContainer.Add(SessionCookie);
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
#if DEBUG
                Debug.WriteLine("Result: " + resultString);
#endif
                return resultString;
            }
            catch (WebException e)
            {
                var error = ReadContentFromResult((HttpWebResponse)e.Response);
                return error;
            }
            finally
            {
                stopwatch.Stop();
                Trace.WriteLine(string.Format("Executed GET {0}ms", stopwatch.Elapsed.TotalMilliseconds));
            }
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

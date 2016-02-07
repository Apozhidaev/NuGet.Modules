using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace NuGet.Modules.Redirect
{
    public class HttpRedirect
    {
        private static readonly HashSet<string> RequestFilter = new HashSet<string>();
        private static readonly HashSet<string> ResponseFilter = new HashSet<string>();
        private readonly HttpListener _listener;
        private readonly Dictionary<string, Dictionary<Regex, string>> _rules;
        private readonly Uri _toUrl;

        static HttpRedirect()
        {
            RequestFilter.Add("Accept-Encoding");
            RequestFilter.Add("Content-Length");
            RequestFilter.Add("Content-Type");
            RequestFilter.Add("Connection");

            ResponseFilter.Add("Transfer-Encoding");
            ResponseFilter.Add("Content-Encoding");
            ResponseFilter.Add("Connection");
        }

        public HttpRedirect(IEnumerable<string> froms, string to,
            Dictionary<string, Dictionary<Regex, string>> rules = null)
        {
            _rules = rules ?? new Dictionary<string, Dictionary<Regex, string>>();
            _listener = new HttpListener();
            foreach (var url in froms)
            {
                _listener.Prefixes.Add(url);
            }
            _toUrl = new Uri(to);
        }

        public void Start()
        {
            _listener.Start();
            Listen();
        }

        public void Stop()
        {
            _listener.Stop();
        }

        private async void Listen()
        {
            while (true)
            {
                try
                {
                    ProcessRequestAsync(await _listener.GetContextAsync());
                }
                catch (HttpListenerException e)
                {
                    Debug.WriteLine(e.GetBaseException().Message, "HttpListenerException");
                    break;
                }
                catch (InvalidOperationException e)
                {
                    Debug.WriteLine(e.GetBaseException().Message, "InvalidOperationException");
                    break;
                }
            }
        }

        private async void ProcessRequestAsync(HttpListenerContext context)
        {
            await Task.Yield();
            var cookieContainer = new CookieContainer();
            using (var handler = new HttpClientHandler {CookieContainer = cookieContainer})
            using (var client = new HttpClient(handler) {BaseAddress = _toUrl})
            {
                foreach (Cookie cookie in context.Request.Cookies)
                {
                    cookieContainer.Add(_toUrl, cookie);
                }
                var message = ToMessage(context.Request, _toUrl);
                var response = await client.SendAsync(message);
                await CopyFrom(context.Response, response, _rules);
                context.Response.Close();
            }
        }

        private static async Task CopyFrom(HttpListenerResponse response, HttpResponseMessage message,
            IReadOnlyDictionary<string, Dictionary<Regex, string>> rules)
        {
            response.StatusCode = (int) message.StatusCode;
            foreach (var httpResponseHeader in message.Headers.Where(header => !ResponseFilter.Contains(header.Key)))
            {
                foreach (var value in httpResponseHeader.Value)
                {
                    response.AddHeader(httpResponseHeader.Key, value);
                }
            }
            foreach (
                var httpResponseHeader in message.Content.Headers.Where(header => !ResponseFilter.Contains(header.Key)))
            {
                foreach (var value in httpResponseHeader.Value)
                {
                    response.AddHeader(httpResponseHeader.Key, value);
                }
            }
            response.SendChunked = false;
            response.KeepAlive = false;

            var bytes = await message.Content.ReadAsByteArrayAsync();

            if (bytes.Length <= 0) return;

            if (rules.ContainsKey(message.Content.Headers.ContentType.MediaType))
            {
                var rule = rules[message.Content.Headers.ContentType.MediaType];
                var charSet = message.Content.Headers.ContentType.CharSet;
                if (charSet.Equals("cp1251", StringComparison.InvariantCultureIgnoreCase))
                {
                    charSet = "windows-1251";
                }
                var encoding = string.IsNullOrEmpty(charSet) ? Encoding.UTF8 : Encoding.GetEncoding(charSet);
                var content = encoding.GetString(bytes);
                content = rule.Aggregate(content, (current, pattern) => pattern.Key.Replace(current, pattern.Value));
                bytes = encoding.GetBytes(content);
            }

            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static HttpRequestMessage ToMessage(HttpListenerRequest request, Uri url)
        {
            var message = new HttpRequestMessage();
            var uriBuilder = new UriBuilder(request.Url)
            {
                Scheme = url.Scheme,
                Host = url.Host,
                Port = url.Port
            };
            foreach (var headerName in request.Headers.AllKeys.Where(header => !RequestFilter.Contains(header)))
            {
                var headerValues = request.Headers.GetValues(headerName);
                if (!message.Headers.TryAddWithoutValidation(headerName, headerValues))
                {
                    message.Content.Headers.TryAddWithoutValidation(headerName, headerValues);
                }
            }
            message.Method = new HttpMethod(request.HttpMethod);
            message.RequestUri = uriBuilder.Uri;
            if (request.ContentLength64 > 0)
            {
                message.Content = new StreamContent(request.InputStream);
            }
            message.Headers.Host = url.Host;
            if (!string.IsNullOrEmpty(request.ContentType))
            {
                message.Content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
            }
            message.Headers.ConnectionClose = true;
            message.Headers.AcceptEncoding.Clear();
            return message;
        }
    }
}
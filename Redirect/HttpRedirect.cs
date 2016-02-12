using System;
using System.Collections.Generic;
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
    public sealed class HttpRedirect
    {
        private static readonly HashSet<string> RequestFilter = new HashSet<string>();
        private static readonly HashSet<string> ResponseFilter = new HashSet<string>();
        private readonly Dictionary<string, Dictionary<Regex, string>> _contentRules;
        private readonly HttpListener _listener;
        private readonly Dictionary<Regex, string> _queryRules;
        private readonly Uri _toUrl;
        private bool _isListening;

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

        public HttpRedirect(RedirectSettings settings)
        {
            _queryRules = settings.QueryRules ?? new Dictionary<Regex, string>();
            _contentRules = settings.ContentRules ?? new Dictionary<string, Dictionary<Regex, string>>();
            _listener = new HttpListener();
            foreach (var url in settings.Froms)
            {
                _listener.Prefixes.Add(url);
            }
            _toUrl = new Uri(settings.To);
        }

        public EventHandler<ProcessRequestEventArgs> ProcessRequestException;

        public void Start()
        {
            _isListening = true;
            _listener.Start();
            Listen();
        }

        public void Stop()
        {
            _isListening = false;
            _listener.Stop();
        }

        private void OnProcessRequestException(Exception exception)
        {
            var handler = ProcessRequestException;
            handler?.Invoke(this, new ProcessRequestEventArgs(exception));
        }

        private async void Listen()
        {
            while (_isListening)
            {
                try
                {
                    await ProcessRequestAsync(await _listener.GetContextAsync());
                }
                catch (Exception e)
                {
                    OnProcessRequestException(e);
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            await Task.Yield();
            var cookieContainer = new CookieContainer();
            using (context.Response)
            using (var handler = new HttpClientHandler { CookieContainer = cookieContainer })
            using (var client = new HttpClient(handler) { BaseAddress = _toUrl })
            {
                foreach (Cookie cookie in context.Request.Cookies)
                {
                    cookieContainer.Add(_toUrl, cookie);
                }
                var message = ToMessage(context.Request, _toUrl, _queryRules);
                var response = await client.SendAsync(message);
                await CopyFrom(context.Response, response, _contentRules);
                context.Response.Close();
            }
        }

        private static async Task CopyFrom(HttpListenerResponse response, HttpResponseMessage message,
            IReadOnlyDictionary<string, Dictionary<Regex, string>> rules)
        {
            response.StatusCode = (int)message.StatusCode;
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

            if (message.Content.Headers.ContentType != null && rules.ContainsKey(message.Content.Headers.ContentType.MediaType))
            {
                var rule = rules[message.Content.Headers.ContentType.MediaType];
                var encoding = GetEncoding(message.Content.Headers.ContentType.CharSet);
                var content = encoding.GetString(bytes);
                content = Replace(content, rule);
                bytes = encoding.GetBytes(content);
            }

            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static HttpRequestMessage ToMessage(HttpListenerRequest request, Uri url,
            Dictionary<Regex, string> queryRules)
        {
            var message = new HttpRequestMessage();

            var query = request.Url.Query;
            if (!string.IsNullOrEmpty(query))
            {
                var parameters = ParseQueryString(query);
                foreach (var parameter in parameters)
                {
                    parameter.Name = Replace(parameter.Name, queryRules);
                    parameter.Value = Replace(parameter.Value, queryRules);
                }
                query = parameters.Aggregate("?", (current, parameter) => $"{current}&{parameter.ToString()}");
            }
            var uriBuilder = new UriBuilder(request.Url)
            {
                Scheme = url.Scheme,
                Host = url.Host,
                Port = url.Port,
                Query = query ?? ""
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

        private static Encoding GetEncoding(string charSet)
        {
            if (!string.IsNullOrEmpty(charSet))
            {
                if (charSet.Equals("cp1251", StringComparison.InvariantCultureIgnoreCase))
                {
                    charSet = "windows-1251";
                }
                return Encoding.GetEncoding(charSet);
            }
            return Encoding.UTF8;
        }

        private static string Replace(string input, IReadOnlyDictionary<Regex, string> rules)
        {
            return rules.Aggregate(input, (current, pattern) => pattern.Key.Replace(current, pattern.Value));
        }

        private static List<QueryParam> ParseQueryString(string query)
        {
            var list = new List<QueryParam>();
            int l = query?.Length ?? 0;
            int i = 0;

            while (i < l)
            {
                int si = i;
                int ti = -1;
                
                while (i < l)
                {
                    char ch = query[i];

                    if (ch == '=')
                    {
                        if (ti < 0)
                            ti = i;
                    }
                    else if (ch == '&')
                    {
                        break;
                    }

                    i++;
                }

                string name = string.Empty;
                string value;

                if (ti >= 0)
                {
                    name = query.Substring(si, ti - si);
                    value = query.Substring(ti + 1, i - ti - 1);
                }
                else {
                    value = query.Substring(si, i - si);
                }

                list.Add(new QueryParam(WebUtility.UrlDecode(name), WebUtility.UrlDecode(value)));

                // trailing '&'

                if (i == l - 1 && query[i] == '&')
                    list.Add(new QueryParam(string.Empty, string.Empty));

                i++;
            }
            return list;
        }

        private class QueryParam
        {
            public QueryParam(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; set; }
            public string Value { get; set; }

            public override string ToString()
            {
                if (string.IsNullOrEmpty(Name))
                {
                    if (string.IsNullOrEmpty(Value)) return string.Empty;
                    return WebUtility.UrlEncode(Value) ?? string.Empty;
                }
                return $"{WebUtility.UrlEncode(Name)}={WebUtility.UrlEncode(Value)}";
            }
        }
    }
}
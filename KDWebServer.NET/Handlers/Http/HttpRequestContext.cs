using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;
using HttpListenerContext = WebSocketSharp.Net.HttpListenerContext;

namespace KDWebServer.Handlers.Http
{
  [PublicAPI]
  public class HttpRequestContext
  {
    public readonly HttpListenerContext HttpContext;

    public string Path => HttpContext.Request.Url.AbsolutePath;

    public HttpMethod HttpMethod { get; }

    // Routing
    public Dictionary<string, object> Params { get; set; }

    // Params
    public QueryStringValuesCollection QueryString { get; }

    // Request
    public string ForwardedUri { get; }
    public System.Net.IPAddress RemoteEndpoint { get; }

    // Headers
    public QueryStringValuesCollection Headers { get; }

    // Content
    public byte[] RawData { get; set; }
    public QueryStringValuesCollection FormData { get; set; }
    public JToken JsonData { get; set; }
    public XDocument XmlData { get; set; }

    public HttpRequestContext(HttpListenerContext httpContext, IPAddress remoteEndpoint)
    {
      HttpContext = httpContext;

      QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.QueryString);
      Headers = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Headers);

      RemoteEndpoint = remoteEndpoint;
      ForwardedUri = Headers.GetStringOrDefault("X-Forwarded-Uri", null);

      HttpMethod = new HttpMethod(httpContext.Request.HttpMethod);
    }

    public string ReadAsString() => RawData == null ? null : HttpContext.Request.ContentEncoding.GetString(RawData);
  }
}
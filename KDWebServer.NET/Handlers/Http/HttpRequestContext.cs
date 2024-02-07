using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

namespace KDWebServer.Handlers.Http;

[PublicAPI]
public class HttpRequestContext
{
  public readonly HttpListenerContext HttpContext;

  public string Path => HttpContext.Request.Url.AbsolutePath;
  public string ForwardedUri => Headers.GetStringOrDefault("X-Forwarded-Uri", null);
  public System.Net.IPAddress RemoteEndpoint { get; }

  public HttpMethod HttpMethod { get; }

  // Routing
  public Dictionary<string, object> Params { get; set; }

  // Params
  public QueryStringValuesCollection QueryString { get; }

  // Headers
  public QueryStringValuesCollection Headers { get; }

  // Content
  public byte[] RawData { get; set; }
  public QueryStringValuesCollection FormData { get; set; }
  public JToken JsonData { get; set; }
  public XDocument XmlData { get; set; }

  internal HttpRequestContext(HttpListenerContext httpContext, IPAddress remoteEndpoint, RequestDispatcher.RouteEndpointMatch match)
  {
    HttpContext = httpContext;

    HttpMethod = new HttpMethod(httpContext.Request.HttpMethod);

    Params = match.RouteParams;

    QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.QueryString);

    Headers = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Headers);

    RemoteEndpoint = remoteEndpoint;
  }

  public string ReadAsString() => RawData == null ? null : HttpContext.Request.ContentEncoding.GetString(RawData);
}
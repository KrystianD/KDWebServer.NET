using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

namespace KDWebServer.Handlers.Http;

[PublicAPI]
public class HttpRequestContext : IRequestContext
{
  public HttpListenerContext HttpContext { get; }
  public CancellationToken Token { get; }

  public string Path => HttpContext.Request.Url!.AbsolutePath;
  public string? ForwardedUri => Headers.TryGetString("X-Forwarded-Uri", out var value) ? value : null;
  public IPAddress RemoteEndpoint { get; }

  public HttpMethod HttpMethod { get; }

  // Routing
  public Dictionary<string, object> Params { get; set; }

  // Params
  public QueryStringValuesCollection QueryString { get; }

  // Headers
  public QueryStringValuesCollection Headers { get; }

  // Content
  public byte[] RawData { get; set; }
  public QueryStringValuesCollection? FormData { get; set; }
  public JToken? JsonData { get; set; }
  public XDocument? XmlData { get; set; }

  internal HttpRequestContext(HttpListenerContext httpContext, IPAddress remoteEndpoint, RequestDispatcher.RouteEndpointMatch match, byte[] rawData, CancellationToken token)
  {
    HttpContext = httpContext;
    Token = token;

    HttpMethod = new HttpMethod(httpContext.Request.HttpMethod);

    Params = match.RouteParams;

    QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.QueryString);

    Headers = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Headers);

    RemoteEndpoint = remoteEndpoint;

    RawData = rawData;
  }

  public string ReadAsString() => HttpContext.Request.ContentEncoding.GetString(RawData);
}
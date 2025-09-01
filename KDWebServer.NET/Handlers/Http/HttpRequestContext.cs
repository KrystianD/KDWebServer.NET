using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace KDWebServer.Handlers.Http;

[PublicAPI]
public class HttpRequestContext : IRequestContext
{
  public HttpContext HttpContext { get; }
  public CancellationToken Token { get; }

  public string Path => HttpContext.Request.Path.Value;
  public IPAddress RemoteEndpoint { get; }
  public string RawUrl { get; }

  public HttpMethod HttpMethod { get; }

  // Routing
  public Dictionary<string, object> Params { get; set; }

  // Params
  public QueryStringValuesCollection QueryString { get; }

  // Content
  public byte[] RawData { get; set; }
  public QueryStringValuesCollection? FormData { get; set; }
  public JToken? JsonData { get; set; }
  public XDocument? XmlData { get; set; }

  internal HttpRequestContext(HttpContext httpContext, IPAddress remoteEndpoint, string rawUrl, RequestDispatcher.RouteEndpointMatch match, byte[] rawData, CancellationToken token)
  {
    HttpContext = httpContext;
    Token = token;

    HttpMethod = new HttpMethod(httpContext.Request.Method);

    Params = match.RouteParams;

    QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Query);

    RemoteEndpoint = remoteEndpoint;
    RawUrl = rawUrl;

    RawData = rawData;
  }

  // public string ReadAsString() => HttpContext.Request.ContentEncoding.GetString(RawData);
}
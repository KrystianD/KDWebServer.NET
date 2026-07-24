using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using JetBrains.Annotations;
using KDWebServer.Auth;
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

  // Auth
  public IAuthState AuthState { get; init; }

  // Content
  public byte[] RawData { get; set; }
  public QueryStringValuesCollection? FormData { get; set; }
  public JToken? JsonData { get; set; }
  public XDocument? XmlData { get; set; }
  
  internal HttpRequestContext(InternalRequestContext ictx, byte[] rawData, CancellationToken token)
  {
    HttpContext = ictx.HttpContext;
    Token = token;

    HttpMethod = new HttpMethod(ictx.HttpContext.Request.Method);

    Params = ictx.Match.RouteParams;

    QueryString = QueryStringValuesCollection.FromNameValueCollection(ictx.HttpContext.Request.Query);

    AuthState = ictx.AuthState;

    RemoteEndpoint = ictx.RemoteEndpoint;
    RawUrl = ictx.RawUrl;

    RawData = rawData;
  }

  // public string ReadAsString() => HttpContext.Request.ContentEncoding.GetString(RawData);
}
﻿using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using HttpListenerContext = WebSocketSharp.Net.HttpListenerContext;

namespace KDWebServer
{
  public class WebServerRequestContext
  {
    public HttpListenerContext httpContext;

    public string Path => httpContext.Request.Url.AbsolutePath;

    private string requestPayload;
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

    public WebServerRequestContext(HttpListenerContext httpContext, IPAddress remoteEndpoint)
    {
      this.httpContext = httpContext;

      QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.QueryString);
      Headers = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Headers);

      RemoteEndpoint = remoteEndpoint;
      ForwardedUri = Headers.GetStringOrDefault("X-Forwarded-Uri", null);

      HttpMethod = new HttpMethod(httpContext.Request.HttpMethod);
    }

    public string ReadAsString() => RawData == null ? null : httpContext.Request.ContentEncoding.GetString(RawData);
  }
}
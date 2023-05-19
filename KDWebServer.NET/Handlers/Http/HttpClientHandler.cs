using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mime;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Fluent;

namespace KDWebServer.Handlers.Http
{
  public class HttpClientHandler
  {
    private static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

    private readonly HttpListenerContext _httpContext;
    private readonly RequestDispatcher.RouteEndpointMatch Match;
    public long ProcessingTime;

    private WebServer WebServer { get; }
    public ILogger Logger { get; }
    public string ClientId { get; }
    private IPAddress RemoteEndpoint { get; }

    internal HttpClientHandler(WebServer webServer, HttpListenerContext httpContext, IPAddress remoteEndpoint, string clientId, RequestDispatcher.RouteEndpointMatch match)
    {
      _httpContext = httpContext;
      WebServer = webServer;
      Logger = webServer.LogFactory?.GetLogger("webserver.http") ?? LogManager.LogFactory.CreateNullLogger();

      RemoteEndpoint = remoteEndpoint;
      ClientId = clientId;
      Match = match;
    }

    public async Task Handle(Dictionary<string, object> props)
    {
      HttpRequestContext ctx = new HttpRequestContext(_httpContext, RemoteEndpoint, Match);

      _httpContext.Response.AppendHeader("Access-Control-Allow-Origin", "*");

      await ReadPayload(ctx);

      props.Add("content_type", _httpContext.Request.ContentType);
      props.Add("content_length", _httpContext.Request.ContentLength64);
      if (_httpContext.Request.ContentType != null) {
        var parsedContent = ProcessKnownTypes(ctx);
        props.Add("content", WebServer.LoggerConfig.LogPayloads ? parsedContent : "<skipped>");
      }

      var ep = Match.Endpoint;

      Logger.Trace()
            .Message($"[{ClientId}] New HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.AbsolutePath}")
            .Properties(props)
            .Write();

      Stopwatch timer = new Stopwatch();
      timer.Start();
      try {
        IWebServerResponse response;
        try {
          response = await ep.HttpCallback(ctx);
        }
        catch (IWebServerResponse r) {
          response = r;
        }

        ProcessingTime = timer.ElapsedMilliseconds;

        if (response == null) {
          _httpContext.Response.StatusCode = 200;
          _httpContext.Response.ContentLength64 = 0;
        }
        else {
          foreach (string responseHeader in response._headers)
            _httpContext.Response.Headers.Add(responseHeader, response._headers[responseHeader]);

          await response.WriteToResponse(this, _httpContext.Response, this.WebServer.LoggerConfig);
        }
      }
      catch (Exception e) {
        Logger.Error()
              .Message($"[{ClientId}] Error during handling HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.AbsolutePath}")
              .Properties(props)
              .Property("status_code", 500)
              .Exception(e)
              .Write();

        _httpContext.Response.StatusCode = 500;
      }
    }

    private static async Task ReadPayload(HttpRequestContext ctx)
    {
      var httpContext = ctx.HttpContext;

      if (!httpContext.Request.HasEntityBody)
        return;

      using var ms = new MemoryStream();

      await Task.Run(() => httpContext.Request.InputStream.CopyTo(ms)); // CopyToAsync doesn't work properly in WebSocketSharp (PlatformNotSupportedException)

      ctx.RawData = ms.ToArray();
    }

    private static object ProcessKnownTypes(HttpRequestContext ctx)
    {
      ContentType ct;

      var httpContext = ctx.HttpContext;

      try { ct = new ContentType(httpContext.Request.ContentType); }
      catch (FormatException) { return null; }

      string payload;

      switch (ct.MediaType) {
        case "application/x-www-form-urlencoded":
          if (!httpContext.Request.HasEntityBody)
            return "(empty)";

          payload = httpContext.Request.ContentEncoding.GetString(ctx.RawData);

          ctx.FormData = QueryStringValuesCollection.Parse(payload);
          return ctx.FormData;

        case "application/json":
          if (!httpContext.Request.HasEntityBody)
            return "(empty)";

          payload = httpContext.Request.ContentEncoding.GetString(ctx.RawData);

          ctx.JsonData = JsonConvert.DeserializeObject<JToken>(payload, JsonSerializerSettings);
          return ctx.JsonData;

        case "text/xml":
          if (!httpContext.Request.HasEntityBody)
            return "(empty)";

          payload = httpContext.Request.ContentEncoding.GetString(ctx.RawData);

          ctx.XmlData = XDocument.Parse(payload);
          return ctx.XmlData;

        default:
          return "(unknown-type)";
      }
    }
  }
}
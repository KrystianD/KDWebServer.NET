using System;
using System.Collections.Generic;
using System.Diagnostics;
using WebSocketSharp.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Security.Authentication;
using System.Threading.Tasks;
using System.Xml.Linq;
using KDLib;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Fluent;

namespace KDWebServer
{
  public class WebServerClientHandler
  {
    private readonly HttpListenerContext _httpContext;
    public long ProcessingTime;

    public WebServer WebServer { get; }
    public NLog.Logger Logger { get; }
    public string ClientId { get; }
    public System.Net.IPAddress RemoteEndpoint { get; }

    public WebServerClientHandler(WebServer webServer, HttpListenerContext httpContext)
    {
      _httpContext = httpContext;
      WebServer = webServer;
      Logger = webServer.LogFactory.GetLogger<NLog.Logger>("webserver.client");

      string shortId = StringUtils.GenerateRandomString(4);
      RemoteEndpoint = Utils.GetClientIp(_httpContext);
      ClientId = $"{RemoteEndpoint} {shortId}";
    }

    private (WebServerUtils.RouteMatch RouteMatch, WebServer.EndpointDefinition Endpoint) MatchRoutes(string path, HttpMethod method)
    {
      int bestScore = -1;
      WebServerUtils.RouteMatch bestRoute = null;
      WebServer.EndpointDefinition bestEndpoint = null;

      foreach (var pair in WebServer.Endpoints) {
        var route = pair.Key;
        var endpointDefinition = pair.Value;

        if (!route.Methods.Contains(method))
          continue;

        WebServerUtils.RouteMatch m;
        if (route.TryMatch(path, out m)) {
          if (bestScore == -1 || route.Score > bestScore) {
            bestScore = route.Score;
            bestRoute = m;
            bestEndpoint = endpointDefinition;
          }
        }
      }

      return bestScore == -1 ? (null, null) : (bestRoute, bestEndpoint);
    }

    public async void Handle()
    {
      WebServerRequestContext ctx = new WebServerRequestContext(_httpContext);

      var httpContext = _httpContext;

      httpContext.Response.AddHeader("Access-Control-Allow-Origin", "*");

      var props = new Dictionary<string, string>() {
          ["method"] = _httpContext.Request.HttpMethod,
          ["path"] = _httpContext.Request.Url.AbsolutePath,
          ["query"] = _httpContext.Request.Url.Query,
      };

      string bodyStr = null;
      using (MappedDiagnosticsLogicalContext.SetScoped("client_id", ClientId))
      using (MappedDiagnosticsLogicalContext.SetScoped("remote_ip", RemoteEndpoint)) {
        try {
          var match = MatchRoutes(ctx.Path, ctx.HttpMethod);
          if (match.RouteMatch == null) {
            Logger.Trace()
                  .Message($"[{ClientId}] New invalid HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
                  .Properties(props)
                  .Write();

            httpContext.Response.StatusCode = 404;
            httpContext.Response.OutputStream.Close();
            return;
          }

          var ep = match.Endpoint;
          ctx.Params = match.RouteMatch.Params;

          // Parse request
          if (httpContext.Request.ContentType != null) {
            bodyStr = await ParseKnownTypes(httpContext, ctx);
          }

          Logger.Trace()
                .Message($"[{ClientId}] New HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
                .Properties(props)
                .Property("content", bodyStr)
                .Write();

          Stopwatch timer = new Stopwatch();
          timer.Start();
          var response = await ep.Callback(ctx);

          ProcessingTime = timer.ElapsedMilliseconds;

          if (response == null) {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.ContentLength64 = 0;
          }
          else {
            foreach (string responseHeader in response._headers)
              httpContext.Response.Headers.Add(responseHeader, response._headers[responseHeader]);

            await response.WriteToResponse(this, httpContext.Response);
          }
        }
        catch (UnauthorizedException) {
          Logger.Info()
                .Message($"[{ClientId}] Unauthorized HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
                .Properties(props)
                .Property("content", bodyStr)
                .Write();

          httpContext.Response.StatusCode = 401;
        }
        catch (Exception e) {
          Logger.Error()
                .Message($"[{ClientId}] Error during handling HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
                .Properties(props)
                .Property("content", bodyStr)
                .Exception(e)
                .Write();

          httpContext.Response.StatusCode = 500;
        }

        try {
          httpContext.Response.OutputStream.Close();
        }
        catch { // ignored
        }
      }
    }

    private static async Task<string> ParseKnownTypes(HttpListenerContext httpContext, WebServerRequestContext ctx)
    {
      ContentType ct;

      try { ct = new ContentType(httpContext.Request.ContentType); }
      catch (FormatException) { return null; }

      string payload;

      switch (ct.MediaType) {
        case "application/x-www-form-urlencoded":
          payload = await ctx.ReadAsString();
          if (payload == null)
            return "(empty)";

          ctx.FormData = QueryStringValuesCollection.Parse(payload);
          return Uri.UnescapeDataString(payload);

        case "application/json":
          payload = await ctx.ReadAsString();
          if (payload == null)
            return "(empty)";

          ctx.JsonData = JToken.Parse(payload);
          return ctx.JsonData.ToString(Newtonsoft.Json.Formatting.Indented);

        case "text/xml":
          payload = await ctx.ReadAsString();
          if (payload == null)
            return "(empty)";

          ctx.XmlData = XDocument.Parse(payload);
          return ctx.XmlData.ToString(SaveOptions.None);

        default:
          return null;
      }
    }
  }
}
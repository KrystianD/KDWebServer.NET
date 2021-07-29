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

    private (Router.RouteMatch RouteMatch, WebServer.EndpointDefinition Endpoint) MatchRoutes(string path, HttpMethod method)
    {
      int bestScore = -1;
      Router.RouteMatch bestRoute = null;
      WebServer.EndpointDefinition bestEndpoint = null;

      foreach (var pair in WebServer.Endpoints) {
        var route = pair.Key;
        var endpointDefinition = pair.Value;

        if (!route.Methods.Contains(method))
          continue;

        Router.RouteMatch m;
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

      var props = new Dictionary<string, object>() {
          ["method"] = _httpContext.Request.HttpMethod,
          ["path"] = _httpContext.Request.Url.AbsolutePath,
      };

      using (MappedDiagnosticsLogicalContext.SetScoped("client_id", ClientId))
      using (MappedDiagnosticsLogicalContext.SetScoped("remote_ip", RemoteEndpoint)) {
        try {
          // Parse request
          props.Add("query", QueryStringValuesCollection.FromNameValueCollection(_httpContext.Request.QueryString).GetAsDictionary());

          props.Add("content_length", httpContext.Request.ContentLength64);
          if (httpContext.Request.ContentType != null)
            props.Add("content", await ParseKnownTypes(httpContext, ctx));

          var match = MatchRoutes(ctx.Path, ctx.HttpMethod);
          if (match.RouteMatch == null) {
            Logger.Trace()
                  .Message($"[{ClientId}] Not found HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.AbsolutePath}")
                  .Properties(props)
                  .Property("status_code", 404)
                  .Write();

            httpContext.Response.StatusCode = 404;
            httpContext.Response.OutputStream.Close();
            return;
          }

          var ep = match.Endpoint;
          ctx.Params = match.RouteMatch.Params;

          Logger.Trace()
                .Message($"[{ClientId}] New HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.AbsolutePath}")
                .Properties(props)
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
                .Property("status_code", 401)
                .Write();

          httpContext.Response.StatusCode = 401;
        }
        catch (Exception e) {
          Logger.Error()
                .Message($"[{ClientId}] Error during handling HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
                .Properties(props)
                .Property("status_code", 500)
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
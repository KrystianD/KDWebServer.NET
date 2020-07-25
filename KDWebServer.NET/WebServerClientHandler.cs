using System;
using System.Diagnostics;
using WebSocketSharp.Net;
using System.Net.Http;
using System.Net.Mime;
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

      string bodyStr = null;
      using (MappedDiagnosticsLogicalContext.SetScoped("client_id", ClientId))
      using (MappedDiagnosticsLogicalContext.SetScoped("remote_ip", RemoteEndpoint))
      using (MappedDiagnosticsLogicalContext.SetScoped("method", _httpContext.Request.HttpMethod))
      using (MappedDiagnosticsLogicalContext.SetScoped("path", _httpContext.Request.Url.AbsolutePath))
      using (MappedDiagnosticsLogicalContext.SetScoped("query", _httpContext.Request.Url.Query)) {
        try {
          var method = WebServerUtils.StringToHttpMethod(httpContext.Request.HttpMethod);
          if (method == HttpMethod.Head) {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.ContentLength64 = 0;
            httpContext.Response.OutputStream.Close();
            return;
          }

          var match = MatchRoutes(ctx.Path, method);
          if (match.RouteMatch == null) {
            Logger.Trace()
                  .Message($"[{ClientId}] new invalid HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
                  .Write();

            httpContext.Response.StatusCode = 404;
            httpContext.Response.OutputStream.Close();
            return;
          }

          var ep = match.Endpoint;
          ctx.Params = match.RouteMatch.Params;

          // Parse request
          if (httpContext.Request.ContentType != null) {
            var ct = new ContentType(httpContext.Request.ContentType);
            if (ct.MediaType == "application/x-www-form-urlencoded") {
              string payload = await ctx.ReadAsString();
              ctx.FormData = QueryStringValuesCollection.Parse(payload);
              bodyStr = Uri.UnescapeDataString(payload);
            }
            else if (ct.MediaType == "application/json") {
              string payload = await ctx.ReadAsString();
              ctx.JsonData = JToken.Parse(payload);
              bodyStr = ctx.JsonData.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            else if (ct.MediaType == "text/xml") {
              string payload = await ctx.ReadAsString();
              ctx.XmlData = XDocument.Parse(payload);
              bodyStr = ctx.XmlData.ToString(SaveOptions.None);
            }
          }

          Logger.Trace()
                .Message($"[{ClientId}] new HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
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
        catch (Exception e) {
          Logger.Error()
                .Message($"[{ClientId}] Error during handling HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
                .Property("content", bodyStr)
                .Exception(e)
                .Write();

          httpContext.Response.StatusCode = 500;
        }

        httpContext.Response.OutputStream.Close();
      }
    }
  }
}
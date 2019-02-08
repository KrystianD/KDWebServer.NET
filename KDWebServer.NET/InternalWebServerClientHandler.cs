using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using KDLib;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Fluent;
using Formatting = Newtonsoft.Json.Formatting;

namespace KDWebServer
{
  public class InternalWebServerClientHandler
  {
    private readonly HttpListenerContext _httpContext;
    public long ProcessingTime;

    public InternalWebServer InternalWebServer { get; }
    public NLog.Logger Logger { get; }
    public string ClientId { get; }
    public IPAddress RemoteEndpoint { get; }

    public InternalWebServerClientHandler(InternalWebServer internalWebServer, HttpListenerContext httpContext)
    {
      _httpContext = httpContext;
      InternalWebServer = internalWebServer;
      Logger = internalWebServer.LogFactory.GetLogger<NLog.Logger>("webserver.client"); 

      string shortId = StringUtils.GenerateRandomString(4);
      RemoteEndpoint = Utils.GetClientIp(_httpContext);
      ClientId = $"{RemoteEndpoint} {shortId}";
    }

    private (WebServerUtils.RouteMatch RouteMatch, InternalWebServer.EndpointDefinition Endpoint) MatchRoutes(string path, HttpMethod method)
    {
      int bestScore = -1;
      WebServerUtils.RouteMatch bestRoute = null;
      InternalWebServer.EndpointDefinition bestEndpoint = null;

      foreach (var (route, endpointDefinition) in InternalWebServer._endpoints) {
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
      using (MappedDiagnosticsLogicalContext.SetScoped("client_id", ClientId)) {
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
                  // .Property("client_id", ClientId)
                  .Property("remote_ip", RemoteEndpoint)
                  .Property("method", _httpContext.Request.HttpMethod)
                  .Property("path", _httpContext.Request.Url.AbsolutePath)
                  .Property("query", _httpContext.Request.Url.Query)
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
              bodyStr = ctx.JsonData.ToString(Formatting.Indented);
            }
            else if (ct.MediaType == "text/xml") {
              string payload = await ctx.ReadAsString();
              ctx.XmlData = XDocument.Parse(payload);
              bodyStr = ctx.XmlData.ToString(SaveOptions.None);
            }
          }

          Logger.Trace()
                .Message($"[{ClientId}] new HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.PathAndQuery}")
                // .Property("client_id", ClientId)
                .Property("remote_ip", RemoteEndpoint)
                .Property("method", _httpContext.Request.HttpMethod)
                .Property("path", _httpContext.Request.Url.AbsolutePath)
                .Property("query", _httpContext.Request.Url.Query)
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
                // .Property("client_id", ClientId)
                .Property("remote_ip", RemoteEndpoint)
                .Property("method", _httpContext.Request.HttpMethod)
                .Property("path", _httpContext.Request.Url.AbsolutePath)
                .Property("query", _httpContext.Request.Url.Query)
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
using System;
using System.Collections.Generic;
using System.Net.Http;
using KDLib;
using NLog;
using NLog.Fluent;
using WebSocketSharp.Net;

namespace KDWebServer.Handlers
{
  public class RequestDispatcher
  {
    internal class RouteEndpointMatch
    {
      public Router.RouteMatch RouteMatch;
      public WebServer.EndpointDefinition Endpoint;
    }

    private WebServer WebServer { get; }
    private ILogger Logger { get; }

    public RequestDispatcher(WebServer webServer)
    {
      WebServer = webServer;
      Logger = webServer.LogFactory?.GetLogger("webserver.dispatcher") ?? LogManager.LogFactory.CreateNullLogger();
    }

    public async void DispatchRequest(HttpListenerContext httpContext)
    {
      string shortId = StringUtils.GenerateRandomString(4);
      var remoteEndpoint = Utils.GetClientIp(httpContext, WebServer.TrustedProxies);
      var clientId = $"{remoteEndpoint} {shortId}";

      var request = httpContext.Request;
      var response = httpContext.Response;

      var loggingProps = new Dictionary<string, object>() {
          ["method"] = request.HttpMethod,
          ["path"] = request.Url.AbsolutePath,
      };

      using (MappedDiagnosticsLogicalContext.SetScoped("client_id", clientId))
      using (MappedDiagnosticsLogicalContext.SetScoped("remote_ip", remoteEndpoint)) {
        try {
          loggingProps.Add("query", QueryStringValuesCollection.FromNameValueCollection(request.QueryString).GetAsDictionary());

          var match = MatchRoutes(request.Url.AbsolutePath, new HttpMethod(request.HttpMethod));
          if (match == null) {
            Logger.Trace()
                  .Message($"[{clientId}] Not found HTTP request - {request.HttpMethod} {request.Url.AbsolutePath}")
                  .Properties(loggingProps)
                  .Property("status_code", 404)
                  .Write();

            response.StatusCode = 404;
            response.OutputStream.Close();
          }
          else {
            var httpHandler = new HttpClientHandler(WebServer, httpContext, remoteEndpoint, clientId, match);
            await httpHandler.Handle(loggingProps);

            try {
              response.OutputStream.Close();
            }
            catch { // ignored
            }
          }
        }
        catch (RouteInvalidValueProvidedException) {
          Logger.Info()
                .Message($"[{clientId}] Invalid route parameters provided - {request.HttpMethod} {request.Url.PathAndQuery}")
                .Properties(loggingProps)
                .Property("status_code", 400)
                .Write();

          response.StatusCode = 400;
          response.OutputStream.Close();
        }
        catch (Exception e) {
          Logger.Error()
                .Message($"[{clientId}] Error during handling HTTP request - {request.HttpMethod} {request.Url.PathAndQuery}")
                .Properties(loggingProps)
                .Property("status_code", 500)
                .Exception(e)
                .Write();

          response.StatusCode = 500;
          response.OutputStream.Close();
        }
      }
    }

    private RouteEndpointMatch MatchRoutes(string path, HttpMethod method)
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

      return bestScore == -1
          ? null
          : new RouteEndpointMatch() {
              RouteMatch = bestRoute,
              Endpoint = bestEndpoint,
          };
    }
  }
}
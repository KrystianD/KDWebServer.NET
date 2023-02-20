using System.Collections.Generic;
using System.Net.Http;
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
      string shortId = Utils.GenerateRandomString(4);
      var remoteEndpoint = Utils.GetClientIp(httpContext, WebServer.TrustedProxies);
      var clientId = $"{remoteEndpoint} {shortId}";

      var request = httpContext.Request;
      var response = httpContext.Response;

      var reqTypeStr = request.IsWebSocketRequest ? "WS" : "HTTP";
      var logSuffix = $"{request.HttpMethod} {request.Url.AbsolutePath}";

      var loggingProps = new Dictionary<string, object>() {
          ["method"] = request.HttpMethod,
          ["path"] = request.Url.AbsolutePath,
          ["query"] = QueryStringValuesCollection.FromNameValueCollection(request.QueryString).GetAsDictionary(),
      };

      void CloseStream(int? errorCode = null)
      {
        try {
          if (errorCode.HasValue)
            response.StatusCode = errorCode.Value;
          response.OutputStream.Close();
        }
        catch { // ignored
        }
      }

      using (MappedDiagnosticsLogicalContext.SetScoped("client_id", clientId))
      using (MappedDiagnosticsLogicalContext.SetScoped("remote_ip", remoteEndpoint)) {
        RouteEndpointMatch match;
        try {
          match = MatchRoutes(request.Url.AbsolutePath, new HttpMethod(request.HttpMethod));
          if (match == null) {
            Logger.Trace()
                  .Message($"[{clientId}] Not found {reqTypeStr} request - {logSuffix}")
                  .Properties(loggingProps)
                  .Property("status_code", 404)
                  .Write();

            CloseStream(404);
            return;
          }
        }
        catch (RouteInvalidValueProvidedException) {
          Logger.Info()
                .Message($"[{clientId}] Invalid route parameters provided - {logSuffix}")
                .Properties(loggingProps)
                .Property("status_code", 400)
                .Write();

          CloseStream(400);
          return;
        }

        if (match.Endpoint.IsWebsocket) {
          if (request.IsWebSocketRequest) {
            var wsHandler = new Websocket.WebsocketClientHandler(WebServer, httpContext, remoteEndpoint, clientId, match);
            await wsHandler.Handle(loggingProps);
            CloseStream();
          }
          else { // HTTP request to WS endpoint
            Logger.Info()
                  .Message($"[{clientId}] HTTP request to WS endpoint - {logSuffix}")
                  .Properties(loggingProps)
                  .Property("status_code", 426)
                  .Write();

            CloseStream(426);
          }
        }
        else {
          if (request.IsWebSocketRequest) { // WS request to HTTP endpoint
            Logger.Info()
                  .Message($"[{clientId}] WS request to HTTP endpoint - {logSuffix}")
                  .Properties(loggingProps)
                  .Property("status_code", 405)
                  .Write();

            CloseStream(405);
          }
          else {
            var httpHandler = new Http.HttpClientHandler(WebServer, httpContext, remoteEndpoint, clientId, match);
            await httpHandler.Handle(loggingProps);
            CloseStream();
          }
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
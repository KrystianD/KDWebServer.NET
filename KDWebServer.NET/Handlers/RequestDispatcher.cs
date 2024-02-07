using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using KDWebServer.Exceptions;
using NLog;
using NLog.Fluent;

namespace KDWebServer.Handlers;

public class RequestDispatcher
{
  internal record RouteEndpointMatch(
      WebServer.EndpointDefinition Endpoint,
      Dictionary<string, object> RouteParams);

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

    var loggingProps = new Dictionary<string, object?>() {
        ["query"] = QueryStringValuesCollection.FromNameValueCollection(request.QueryString).GetAsDictionary(),
    };

    void CloseStream(int? errorCode = null, string? errorMessage = null)
    {
      try {
        if (errorCode.HasValue) {
          response.StatusCode = errorCode.Value;
        }

        if (errorMessage != null) {
          byte[] resp = Encoding.UTF8.GetBytes(errorMessage);
          response.ContentType = "text/plain";
          response.OutputStream.Write(resp, 0, resp.Length);
        }

        response.OutputStream.Close();
      }
      catch { // ignored
      }
    }

    using (MappedDiagnosticsLogicalContext.SetScoped("method", request.HttpMethod))
    using (MappedDiagnosticsLogicalContext.SetScoped("path", request.Url.AbsolutePath))
    using (MappedDiagnosticsLogicalContext.SetScoped("client_id", clientId))
    using (MappedDiagnosticsLogicalContext.SetScoped("remote_ip", remoteEndpoint)) {
      RouteEndpointMatch? match;
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
      catch (RouteInvalidValueProvidedException e) {
        Logger.Info()
              .Message($"[{clientId}] Invalid route parameters provided - {logSuffix}")
              .Properties(loggingProps)
              .Property("status_code", 400)
              .Write();

        CloseStream(400, e.Message);
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

  private record RouteMatch(int Score, Router.RouteMatch Route, WebServer.EndpointDefinition Endpoint);

  private RouteEndpointMatch? MatchRoutes(string path, HttpMethod method)
  {
    RouteMatch? bestMatch = null;

    foreach (var pair in WebServer.Endpoints) {
      var route = pair.Key;
      var endpointDefinition = pair.Value;

      if (!route.Methods.Contains(method))
        continue;

      if (route.TryMatch(path, out var m) && (bestMatch is null || route.Score > bestMatch.Score))
        bestMatch = new RouteMatch(route.Score, m, endpointDefinition);
    }

    if (bestMatch == null) {
      return null;
    }
    else {
      bestMatch.Route.ParseParams(out var routeParams);

      return new RouteEndpointMatch(bestMatch.Endpoint, routeParams);
    }
  }
}
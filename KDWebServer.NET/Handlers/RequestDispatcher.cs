using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using JetBrains.Annotations;
using KDWebServer.Exceptions;
using NLog;

namespace KDWebServer.Handlers;

public class RequestDispatcher
{
  [PublicAPI]
  public record RouteEndpointMatch(
      WebServer.EndpointDefinition Endpoint,
      HttpMethod Method,
      Dictionary<string, object> RouteParams);

  private WebServer WebServer { get; }
  private ILogger Logger { get; }

  public RequestDispatcher(WebServer webServer)
  {
    WebServer = webServer;
    Logger = webServer.LogFactory?.GetLogger("webserver.dispatcher") ?? LogManager.LogFactory.CreateNullLogger();
  }

  public async void DispatchRequest(HttpListenerContext httpContext, DateTime connectionTime, Stopwatch requestTimer)
  {
    string shortId = WebServerUtils.GenerateRandomString(4);
    var remoteEndpoint = WebServerUtils.GetClientIp(httpContext, WebServer.TrustedProxies);
    var clientId = $"{remoteEndpoint} {shortId}";

    var request = httpContext.Request;
    var response = httpContext.Response;

    var reqTypeStr = request.IsWebSocketRequest ? "WS" : "HTTP";
    var logSuffix = $"{request.HttpMethod} {request.Url?.AbsolutePath}";

    var advLogProperties = new Dictionary<string, object?>() {
        ["webserver.query"] = QueryStringValuesCollection.FromNameValueCollection(request.QueryString).GetAsDictionary(),
    };

    foreach (var observer in WebServer.Observers)
      observer.OnNewRequest(httpContext);

    using (ScopeContext.PushProperty("webserver.method", request.HttpMethod))
    using (ScopeContext.PushProperty("webserver.path", request.Url?.AbsolutePath))
    using (ScopeContext.PushProperty("webserver.short_id", shortId))
    using (ScopeContext.PushProperty("webserver.remote_ip", remoteEndpoint)) {
      if (remoteEndpoint == null || request.Url is null) {
        Logger.ForInfoEvent()
              .Message($"[{clientId}] Invalid request - {logSuffix}")
              .Properties(advLogProperties)
              .Property("webserver.status_code", 400)
              .Log();

        Helpers.CloseStream(response, 400, "invalid request");
        return;
      }

      RouteEndpointMatch? match;
      try {
        match = MatchRoutes(request.Url.AbsolutePath, new HttpMethod(request.HttpMethod));
        if (match == null) {
          Logger.ForTraceEvent()
                .Message($"[{clientId}] Not found {reqTypeStr} request - {logSuffix}")
                .Properties(advLogProperties)
                .Property("webserver.status_code", 404)
                .Log();

          Helpers.CloseStream(response, 404);
          return;
        }
      }
      catch (RouteInvalidValueProvidedException e) {
        Logger.ForInfoEvent()
              .Message($"[{clientId}] Invalid route parameters provided - {logSuffix}")
              .Properties(advLogProperties)
              .Property("webserver.status_code", 400)
              .Log();

        Helpers.CloseStream(response, 400, e.Message);
        return;
      }

      foreach (var observer in WebServer.Observers)
        observer.OnRequestMatch(httpContext, match);

      if (match.Endpoint.IsWebsocket) {
        if (request.IsWebSocketRequest) {
          var wsHandler = new Websocket.WebsocketClientHandler(WebServer, httpContext, remoteEndpoint, clientId, connectionTime, requestTimer, match);
          await wsHandler.Handle(advLogProperties).ConfigureAwait(false);
          Helpers.CloseStream(response);
        }
        else { // HTTP request to WS endpoint
          Logger.ForInfoEvent()
                .Message($"[{clientId}] HTTP request to WS endpoint - {logSuffix}")
                .Properties(advLogProperties)
                .Property("webserver.status_code", 426)
                .Log();

          Helpers.CloseStream(response, 426);
        }
      }
      else {
        if (request.IsWebSocketRequest) { // WS request to HTTP endpoint
          Logger.ForInfoEvent()
                .Message($"[{clientId}] WS request to HTTP endpoint - {logSuffix}")
                .Properties(advLogProperties)
                .Property("webserver.status_code", 405)
                .Log();

          Helpers.CloseStream(response, 405);
        }
        else {
          var httpHandler = new Http.HttpClientHandler(WebServer, httpContext, remoteEndpoint, connectionTime, requestTimer, clientId, match);
          await httpHandler.Handle(advLogProperties).ConfigureAwait(false);
          Helpers.CloseStream(response);
        }
      }
    }
  }

  private record RouteMatch(int Score, Router.RouteMatch Route, WebServer.EndpointDefinition Endpoint);

  private RouteEndpointMatch? MatchRoutes(string path, HttpMethod method)
  {
    RouteMatch? bestMatch = null;

    foreach (var (route, endpointDefinition) in WebServer.Endpoints) {
      if (!endpointDefinition.Methods.Contains(method))
        continue;

      if (route.TryMatch(path, out var m) && (bestMatch is null || route.Score > bestMatch.Score))
        bestMatch = new RouteMatch(route.Score, m, endpointDefinition);
    }

    if (bestMatch == null) {
      return null;
    }
    else {
      bestMatch.Route.ParseParams(out var routeParams);

      return new RouteEndpointMatch(bestMatch.Endpoint, method, routeParams);
    }
  }
}
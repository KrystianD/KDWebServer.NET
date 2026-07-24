using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using JetBrains.Annotations;
using KDWebServer.Exceptions;
using Microsoft.AspNetCore.Http;
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

  public async Task DispatchRequest(HttpContext httpContext, DateTime connectionTime, Stopwatch requestTimer)
  {
    var ctx = new InternalRequestContext();
    ctx.HttpContext = httpContext;
    ctx.ConnectionTime = connectionTime;
    ctx.RequestTimer = requestTimer;

    string shortId = WebServerUtils.GenerateRandomString(4);
    ctx.RemoteEndpoint = WebServerUtils.GetClientIp(httpContext, WebServer.TrustedProxies);
    ctx.ClientId = $"{ctx.RemoteEndpoint} {shortId}";
    ctx.RawUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}/{httpContext.Request.Path}{httpContext.Request.QueryString}";

    var request = httpContext.Request;
    var response = httpContext.Response;

    var isWS = httpContext.WebSockets.IsWebSocketRequest;

    var path = Uri.UnescapeDataString(request.Path.Value ?? "");

    var reqTypeStr = isWS ? "WS" : "HTTP";
    var logSuffix = $"{request.Method} {path}";

    var advLogProperties = new Dictionary<string, object?>() {
        ["webserver.query"] = QueryStringValuesCollection.FromNameValueCollection(request.QueryString).GetAsDictionary(),
    };

    foreach (var observer in WebServer.Observers)
      observer.OnNewRequest(httpContext);

    using (ScopeContext.PushProperty("webserver.method", request.Method))
    using (ScopeContext.PushProperty("webserver.path", path))
    using (ScopeContext.PushProperty("webserver.url", ctx.RawUrl))
    using (ScopeContext.PushProperty("webserver.short_id", shortId))
    using (ScopeContext.PushProperty("webserver.remote_ip", ctx.RemoteEndpoint)) {
      // Validate request integrity
      if (ctx.RemoteEndpoint == null) {
        Logger.ForInfoEvent()
              .Message($"[{ctx.ClientId}] Invalid request - {logSuffix}")
              .Properties(advLogProperties)
              .Property("webserver.status_code", 400)
              .Log();

        Helpers.CloseStream(response, 400, "invalid request");
        return;
      }

      // CORS
      if (WebServer.Config.CORSAllowAll) {
        if (!isWS) {
          httpContext.Response.Headers.Append("Access-Control-Allow-Origin", "*");
          if (httpContext.Request.Method == "OPTIONS") {
            httpContext.Response.Headers.Append("Access-Control-Allow-Headers", "*");
            httpContext.Response.Headers.Append("Access-Control-Allow-Methods", "*");
            httpContext.Response.Headers.Append("Access-Control-Max-Age", "86400");
            Helpers.CloseStream(response, 204);
            return;
          }
        }
      }

      // Match route
      try {
        ctx.Match = MatchRoutes(path, new HttpMethod(request.Method));
        if (ctx.Match == null) {
          Logger.ForTraceEvent()
                .Message($"[{ctx.ClientId}] Not found {reqTypeStr} request - {logSuffix}")
                .Properties(advLogProperties)
                .Property("webserver.status_code", 404)
                .Log();

          Helpers.CloseStream(response, 404);
          return;
        }
      }
      catch (RouteInvalidValueProvidedException e) {
        Logger.ForInfoEvent()
              .Message($"[{ctx.ClientId}] Invalid route parameters provided - {logSuffix}")
              .Properties(advLogProperties)
              .Property("webserver.status_code", 400)
              .Log();

        Helpers.CloseStream(response, 400, e.Message);
        return;
      }

      foreach (var observer in WebServer.Observers)
        observer.OnRequestMatch(httpContext, ctx.Match);

      // Auth 
      if (WebServer.Config.Auth.BearerAuthEnabled) {
        if (httpContext.Request.Headers.Authorization is { Count: > 0 } auth && auth[0] is { } str0) {
          var parts = str0.Split(' ', 2);
          if (parts.Length != 2 || !parts[0].Equals("bearer", StringComparison.InvariantCultureIgnoreCase)) {
            Logger.ForInfoEvent()
                  .Message($"[{ctx.ClientId}] Invalid authentication - {logSuffix}")
                  .Properties(advLogProperties)
                  .Property("webserver.status_code", 403)
                  .Log();

            Helpers.CloseStream(response, 403, "invalid authentication");
            return;
          }

          var bearerToken = parts[1];
          ctx.AuthState = await WebServer.Config.Auth.BearerAuthHandler(bearerToken);

          if (ctx is { Match.Endpoint.RequireAuthentication: true, AuthState.IsAuthenticated: false }) {
            Logger.ForInfoEvent()
                  .Message($"[{ctx.ClientId}] Not authenticated - {logSuffix}")
                  .Properties(advLogProperties)
                  .Property("webserver.status_code", 403)
                  .Log();

            Helpers.CloseStream(response, 403, "not authenticated");
            return;
          }
        }
        else {
          if (ctx.Match.Endpoint.RequireAuthentication) {
            Logger.ForInfoEvent()
                  .Message($"[{ctx.ClientId}] No authentication - {logSuffix}")
                  .Properties(advLogProperties)
                  .Property("webserver.status_code", 403)
                  .Log();

            Helpers.CloseStream(response, 403, "no authentication");
            return;
          }
        }
      }

      // Handle
      if (ctx.Match.Endpoint.IsWebsocket) {
        if (isWS) {
          var wsHandler = new Websocket.WebsocketClientHandler(WebServer, ctx);
          await wsHandler.Handle(advLogProperties).ConfigureAwait(false);
          Helpers.CloseStream(response);
        }
        else { // HTTP request to WS endpoint
          Logger.ForInfoEvent()
                .Message($"[{ctx.ClientId}] HTTP request to WS endpoint - {logSuffix}")
                .Properties(advLogProperties)
                .Property("webserver.status_code", 426)
                .Log();

          Helpers.CloseStream(response, 426);
        }
      }
      else {
        if (isWS) { // WS request to HTTP endpoint
          Logger.ForInfoEvent()
                .Message($"[{ctx.ClientId}] WS request to HTTP endpoint - {logSuffix}")
                .Properties(advLogProperties)
                .Property("webserver.status_code", 405)
                .Log();

          Helpers.CloseStream(response, 405);
        }
        else {
          var httpHandler = new Http.HttpClientHandler(WebServer, ctx);
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
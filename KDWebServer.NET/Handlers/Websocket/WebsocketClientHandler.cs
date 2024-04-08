using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Nito.AsyncEx;
using NLog;
using NLog.Fluent;

namespace KDWebServer.Handlers.Websocket;

[PublicAPI]
[SuppressMessage("ReSharper", "MethodHasAsyncOverload")]
public class WebsocketClientHandler
{
  private readonly HttpListenerContext _httpContext;
  private readonly RequestDispatcher.RouteEndpointMatch Match;

  private WebServer WebServer { get; }
  public ILogger Logger { get; }
  public string ClientId { get; }
  private IPAddress RemoteEndpoint { get; }

  internal WebsocketClientHandler(WebServer webServer, HttpListenerContext httpContext, IPAddress remoteEndpoint, string clientId, Stopwatch requestTimer, RequestDispatcher.RouteEndpointMatch match)
  {
    _httpContext = httpContext;
    WebServer = webServer;
    Logger = webServer.LogFactory?.GetLogger("webserver.ws") ?? LogManager.LogFactory.CreateNullLogger();

    RemoteEndpoint = remoteEndpoint;
    ClientId = clientId;
    Match = match;
  }

  public async Task Handle(Dictionary<string, object?> props)
  {
    var wsCtx = await _httpContext.AcceptWebSocketAsync(null!).ConfigureAwait(false);

    var serverShutdownToken = WebServer.ServerShutdownToken;

    var ws = wsCtx.WebSocket;
    WebsocketRequestContext ctx = new WebsocketRequestContext(_httpContext, RemoteEndpoint, Match, ws, WebServer.WebsocketSenderQueueLength, serverShutdownToken);

    var senderQueueToken = CancellationTokenSource.CreateLinkedTokenSource(serverShutdownToken);

    _ = Task.Run(async () => {
      while (!senderQueueToken.Token.IsCancellationRequested) {
        try {
          var msg = ctx.SenderQ.Dequeue(senderQueueToken.Token);
          await ws.SendAsync(msg.Buffer, msg.MessageType, msg.EndOfMessage, senderQueueToken.Token).ConfigureAwait(false);
          msg.OnSent?.Invoke();
        }
        catch (TaskCanceledException) {
        }
      }
    }, senderQueueToken.Token);

    var logSuffix = $"{_httpContext.Request.Url!.AbsolutePath}";

    Logger.ForTraceEvent()
          .Message($"[{ClientId}] New WS request - {logSuffix}")
          .Properties(props)
          .Log();

    try {
      if (WebServer.SynchronizationContext == null)
        await Match.Endpoint.WsCallback!(ctx).ConfigureAwait(false);
      else
        await WebServer.SynchronizationContext.PostAsync(async () => await Match.Endpoint.WsCallback!(ctx)).ConfigureAwait(false);

      try {
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token).ConfigureAwait(false);
      }
      catch {
        ws.Abort();
      }

      Logger.ForTraceEvent()
            .Message($"[{ClientId}] WS handler finished gracefully - {logSuffix}")
            .Properties(props)
            .Log();
    }
    catch (WebSocketException) {
      Logger.ForTraceEvent()
            .Message($"[{ClientId}] WS connection has been closed, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(props)
            .Log();

      senderQueueToken.Cancel();
      ctx.ErrorTcs.TrySetResult(new WebSocketError());
    }
    catch (OperationCanceledException) {
      senderQueueToken.Cancel();
      ctx.ErrorTcs.TrySetResult(new WebSocketError());

      try {
        await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "server is being shut down", new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token).ConfigureAwait(false);
      }
      catch (Exception) {
        ws.Abort();
      }
    }
    catch (Exception e) {
      Logger.ForErrorEvent()
            .Message($"[{ClientId}] Error during handling WS connection - {logSuffix}")
            .Properties(props)
            .Exception(e)
            .Log();

      senderQueueToken.Cancel();
      ctx.ErrorTcs.TrySetResult(new WebSocketDisconnect());

      try {
        await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, null, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token).ConfigureAwait(false);
      }
      catch {
        ws.Abort();
      }
    }
  }
}

[PublicAPI]
public class WebSocketDisconnect : Exception
{
  // public CloseEventArgs CloseEvent { get; }
  //
  // public WebSocketDisconnect(CloseEventArgs closeEvent)
  // {
  //   CloseEvent = closeEvent;
  // }
}

[PublicAPI]
public class WebSocketError : Exception
{
  // public ErrorEventArgs ErrorEvent { get; }
  //
  // public WebSocketError(ErrorEventArgs errorEvent)
  // {
  //   ErrorEvent = errorEvent;
  // }
}
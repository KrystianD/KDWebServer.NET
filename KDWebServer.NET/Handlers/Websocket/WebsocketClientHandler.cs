using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Nito.AsyncEx;
using NLog;

namespace KDWebServer.Handlers.Websocket;

[PublicAPI]
[SuppressMessage("ReSharper", "MethodHasAsyncOverload")]
public class WebsocketClientHandler
{
  private readonly HttpListenerContext _httpContext;
  private readonly DateTime _connectionTime;
  private readonly RequestDispatcher.RouteEndpointMatch Match;

  private WebServer WebServer { get; }
  public ILogger Logger { get; }
  public string ClientId { get; }
  private IPAddress RemoteEndpoint { get; }

  internal WebsocketClientHandler(WebServer webServer, HttpListenerContext httpContext, IPAddress remoteEndpoint, string clientId, DateTime connectionTime, Stopwatch requestTimer, RequestDispatcher.RouteEndpointMatch match)
  {
    _httpContext = httpContext;
    _connectionTime = connectionTime;
    WebServer = webServer;
    Logger = webServer.LogFactory?.GetLogger("webserver.ws") ?? LogManager.LogFactory.CreateNullLogger();

    RemoteEndpoint = remoteEndpoint;
    ClientId = clientId;
    Match = match;
  }

  public async Task Handle(Dictionary<string, object?> advLogProperties)
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
        catch (WebSocketException) {
          senderQueueToken.Cancel();
          ctx.SenderQ.CompleteAdding();
        }
      }
    }, senderQueueToken.Token);

    var logSuffix = $"{_httpContext.Request.Url!.AbsolutePath}";

    Logger.ForTraceEvent()
          .Message($"[{ClientId}] New WS request - {logSuffix}")
          .Properties(advLogProperties)
          .Property("time_conn", $"{(int)(_connectionTime - DateTime.UtcNow).TotalMilliseconds}ms")
          .Log();

    try {
      if (Match.Endpoint.RunOnThreadPool) {
        await Task.Run(async () => await Match.Endpoint.WsCallback!(ctx, senderQueueToken.Token).ConfigureAwait(false), serverShutdownToken).ConfigureAwait(false);
      }
      else if (WebServer.SynchronizationContext == null) {
        await Match.Endpoint.WsCallback!(ctx, senderQueueToken.Token).ConfigureAwait(false);
      }
      else {
        var scope = ScopeContext.GetAllProperties().ToArray();
        await WebServer.SynchronizationContext.PostAsync(async () => {
          using var _ = ScopeContext.PushProperties(scope);
          await Match.Endpoint.WsCallback!(ctx, senderQueueToken.Token);
        }).ConfigureAwait(false);
      }

      try {
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token).ConfigureAwait(false);
      }
      catch {
        ws.Abort();
      }

      Logger.ForTraceEvent()
            .Message($"[{ClientId}] WS handler finished gracefully - {logSuffix}")
            .Properties(advLogProperties)
            .Log();
    }
    catch (WebSocketException) {
      Logger.ForTraceEvent()
            .Message($"[{ClientId}] WS connection has been closed, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(advLogProperties)
            .Log();

      senderQueueToken.Cancel();
    }
    catch (WebSocketDisconnect) {
      Logger.ForTraceEvent()
            .Message($"[{ClientId}] WS connection has been closed, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(advLogProperties)
            .Log();

      senderQueueToken.Cancel();
    }
    catch (TaskCanceledException) when (senderQueueToken.IsCancellationRequested) {
      Logger.ForTraceEvent()
            .Message($"[{ClientId}] WS connection has been closed - {logSuffix}")
            .Properties(advLogProperties)
            .Log();
    }
    catch (Exception e) {
      Logger.ForErrorEvent()
            .Message($"[{ClientId}] Error during handling WS connection - {logSuffix}")
            .Properties(advLogProperties)
            .Exception(e)
            .Log();

      senderQueueToken.Cancel();

      try {
        await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, null, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token).ConfigureAwait(false);
      }
      catch {
        ws.Abort();
      }
    }
    finally {
      ctx.SenderQ.CompleteAdding();

      // ReSharper disable MethodHasAsyncOverloadWithCancellation
      while (ctx.SenderQ.OutputAvailable()) {
        ctx.SenderQ.Dequeue();
      }
      // ReSharper restore MethodHasAsyncOverloadWithCancellation
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
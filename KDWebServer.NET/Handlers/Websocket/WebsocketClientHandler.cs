using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
  public ILogger Logger { get; }

  private WebServer WebServer { get; }
  private readonly InternalRequestContext _ictx;

  internal WebsocketClientHandler(WebServer webServer, InternalRequestContext ictx)
  {
    _ictx = ictx;
    WebServer = webServer;
    Logger = webServer.LogFactory?.GetLogger("webserver.ws") ?? LogManager.LogFactory.CreateNullLogger();
  }

  // ReSharper disable AccessToDisposedClosure
  public async Task Handle(Dictionary<string, object?> advLogProperties)
  {
    var ws = await _ictx.HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

    using var requestCancellationCts = CancellationTokenSource.CreateLinkedTokenSource(_ictx.HttpContext.Response.HttpContext.RequestAborted, WebServer.ServerShutdownToken);
    var requestAbortedToken = requestCancellationCts.Token;

    using var senderQueueToken = CancellationTokenSource.CreateLinkedTokenSource(requestAbortedToken);

    WebsocketRequestContext ctx = new WebsocketRequestContext(_ictx, ws, WebServer.WebsocketSenderQueueLength, senderQueueToken.Token);

    // ReSharper disable AccessToDisposedClosure
    var senderTask = Task.Run(async () => {
      try {
        while (await ctx.SenderQ.Reader.WaitToReadAsync(senderQueueToken.Token)) {
          while (ctx.SenderQ.Reader.TryRead(out var msg)) {
            Interlocked.Add(ref ctx._senderQueueBytes, -msg.Buffer.Length);
            await ws.SendAsync(msg.Buffer, msg.MessageType, msg.EndOfMessage, senderQueueToken.Token).ConfigureAwait(false);
            msg.OnSent?.Invoke();
          }
        }
      }
      catch (OperationCanceledException) {
      }
      catch (Exception) {
        senderQueueToken.Cancel();
        ctx.SenderQ.Writer.TryComplete();
      }
    }, senderQueueToken.Token);
    // ReSharper restore AccessToDisposedClosure

    var logSuffix = $"{_ictx.HttpContext.Request.Path.Value}";

    Logger.ForInfoEvent()
          .Message($"[{_ictx.ClientId}] New WS request - {logSuffix}")
          .Properties(advLogProperties)
          .Property("webserver.time_conn", $"{(int)(_ictx.ConnectionTime - DateTime.UtcNow).TotalMilliseconds}ms")
          .Log();

    // ReSharper disable AccessToDisposedClosure
    try {
      if (_ictx.Match.Endpoint.RunOnThreadPool) {
        await Task.Run(async () => await _ictx.Match.Endpoint.WsCallback!(ctx, senderQueueToken.Token).ConfigureAwait(false), senderQueueToken.Token).ConfigureAwait(false);
      }
      else if (WebServer.SynchronizationContext == null) {
        await _ictx.Match.Endpoint.WsCallback!(ctx, senderQueueToken.Token).ConfigureAwait(false);
      }
      else {
        var scope = ScopeContext.GetAllProperties().ToArray();
        await WebServer.SynchronizationContext.PostAsync(async () => {
          using var _ = ScopeContext.PushProperties(scope);
          await _ictx.Match.Endpoint.WsCallback!(ctx, senderQueueToken.Token);
        }).ConfigureAwait(false);
      }

      if (ws.State != WebSocketState.Closed) {
        try {
          using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
          await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token).ConfigureAwait(false);
        }
        catch {
          ws.Abort();
        }
      }

      Logger.ForInfoEvent()
            .Message($"[{_ictx.ClientId}] WS handler finished gracefully, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(advLogProperties)
            .Log();
    }
    catch (WebSocketException) {
      Logger.ForInfoEvent()
            .Message($"[{_ictx.ClientId}] WS connection has been closed, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(advLogProperties)
            .Log();

      senderQueueToken.Cancel();
    }
    catch (WebSocketDisconnect) {
      Logger.ForInfoEvent()
            .Message($"[{_ictx.ClientId}] WS connection has been closed, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(advLogProperties)
            .Log();

      senderQueueToken.Cancel();
    }
    catch (OperationCanceledException) when (senderQueueToken.IsCancellationRequested) {
      Logger.ForInfoEvent()
            .Message($"[{_ictx.ClientId}] WS connection has been closed - {logSuffix}")
            .Properties(advLogProperties)
            .Log();
    }
    catch (Exception e) {
      Logger.ForErrorEvent()
            .Message($"[{_ictx.ClientId}] Error during handling WS connection - {logSuffix}")
            .Properties(advLogProperties)
            .Exception(e)
            .Log();

      senderQueueToken.Cancel();
      ctx.SenderQ.Writer.TryComplete();
      if (!senderTask.IsCompleted)
        await senderTask.ConfigureAwait(false);

      try {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, null, cts.Token).ConfigureAwait(false);
      }
      catch {
        ws.Abort();
      }
    }
    finally {
      senderQueueToken.Cancel();
      ctx.SenderQ.Writer.TryComplete();
      if (!senderTask.IsCompleted)
        await senderTask.ConfigureAwait(false);
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
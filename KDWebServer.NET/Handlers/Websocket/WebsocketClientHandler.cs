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
using Microsoft.AspNetCore.Http;
using Nito.AsyncEx;
using NLog;

namespace KDWebServer.Handlers.Websocket;

[PublicAPI]
[SuppressMessage("ReSharper", "MethodHasAsyncOverload")]
public class WebsocketClientHandler
{
  private readonly HttpContext _httpContext;
  private readonly DateTime _connectionTime;
  private readonly RequestDispatcher.RouteEndpointMatch Match;

  private WebServer WebServer { get; }
  public ILogger Logger { get; }
  public string RawUrl { get; }
  public string ClientId { get; }
  private IPAddress RemoteEndpoint { get; }

  internal WebsocketClientHandler(WebServer webServer, HttpContext httpContext, IPAddress remoteEndpoint, string rawUrl, string clientId, DateTime connectionTime, Stopwatch requestTimer, RequestDispatcher.RouteEndpointMatch match)
  {
    _httpContext = httpContext;
    _connectionTime = connectionTime;
    WebServer = webServer;
    Logger = webServer.LogFactory?.GetLogger("webserver.ws") ?? LogManager.LogFactory.CreateNullLogger();

    RemoteEndpoint = remoteEndpoint;
    RawUrl = rawUrl;
    ClientId = clientId;
    Match = match;
  }

  // ReSharper disable AccessToDisposedClosure
  public async Task Handle(Dictionary<string, object?> advLogProperties)
  {
    var wsCtx = await _httpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

    var serverShutdownToken = WebServer.ServerShutdownToken;

    var ws = wsCtx;
    WebsocketRequestContext ctx = new WebsocketRequestContext(_httpContext, RemoteEndpoint, RawUrl, Match, ws, WebServer.WebsocketSenderQueueLength, serverShutdownToken);

    using var senderQueueToken = CancellationTokenSource.CreateLinkedTokenSource(serverShutdownToken);

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

    var logSuffix = $"{_httpContext.Request.Path.Value}";

    Logger.ForInfoEvent()
          .Message($"[{ClientId}] New WS request - {logSuffix}")
          .Properties(advLogProperties)
          .Property("webserver.time_conn", $"{(int)(_connectionTime - DateTime.UtcNow).TotalMilliseconds}ms")
          .Log();

    // ReSharper disable AccessToDisposedClosure
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
            .Message($"[{ClientId}] WS handler finished gracefully, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(advLogProperties)
            .Log();
    }
    catch (WebSocketException) {
      Logger.ForInfoEvent()
            .Message($"[{ClientId}] WS connection has been closed, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(advLogProperties)
            .Log();

      senderQueueToken.Cancel();
    }
    catch (WebSocketDisconnect) {
      Logger.ForInfoEvent()
            .Message($"[{ClientId}] WS connection has been closed, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
            .Properties(advLogProperties)
            .Log();

      senderQueueToken.Cancel();
    }
    catch (OperationCanceledException) when (senderQueueToken.IsCancellationRequested) {
      Logger.ForInfoEvent()
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
      ctx.SenderQ.Writer.TryComplete();
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
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NLog;
using NLog.Fluent;

namespace KDWebServer.Handlers.Websocket
{
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

    internal WebsocketClientHandler(WebServer webServer, HttpListenerContext httpContext, IPAddress remoteEndpoint, string clientId, RequestDispatcher.RouteEndpointMatch match)
    {
      _httpContext = httpContext;
      WebServer = webServer;
      Logger = webServer.LogFactory?.GetLogger("webserver.ws") ?? LogManager.LogFactory.CreateNullLogger();

      RemoteEndpoint = remoteEndpoint;
      ClientId = clientId;
      Match = match;
    }

    public async Task Handle(Dictionary<string, object> props)
    {
      var wsCtx = await _httpContext.AcceptWebSocketAsync(null);

      var ws = wsCtx.WebSocket;
      WebsocketRequestContext ctx = new WebsocketRequestContext(_httpContext, RemoteEndpoint, Match, ws, WebServer.WebsocketSenderQueueLength);

      var cts = new CancellationTokenSource();

      var _ = Task.Run(async () => {
        while (!cts.Token.IsCancellationRequested) {
          try {
            var msg = ctx.SenderQ.Dequeue(cts.Token);
            await ws.SendAsync(msg.Buffer, msg.MessageType, msg.EndOfMessage, cts.Token);
            msg.OnSent?.Invoke();
          }
          catch (TaskCanceledException) {
          }
        }
      }, cts.Token);

      var logSuffix = $"{_httpContext.Request.Url.AbsolutePath}";

      Logger.Trace()
            .Message($"[{ClientId}] New WS request - {logSuffix}")
            .Properties(props)
            .Write();

      try {
        await Match.Endpoint.WsCallback(ctx);

        try {
          await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }
        catch {
          ws.Abort();
        }

        Logger.Trace()
              .Message($"[{ClientId}] WS handler finished gracefully - {logSuffix}")
              .Properties(props)
              .Write();
      }
      catch (WebSocketException) {
        Logger.Trace()
              .Message($"[{ClientId}] WS connection has been closed, code: {ws.CloseStatus?.ToString()}, message: {ws.CloseStatusDescription} - {logSuffix}")
              .Properties(props)
              .Write();

        cts.Cancel();
        ctx.ErrorTcs.TrySetResult(new WebSocketError());
      }
      catch (Exception e) {
        Logger.Error()
              .Message($"[{ClientId}] Error during handling WS connection - {logSuffix}")
              .Properties(props)
              .Exception(e)
              .Write();

        cts.Cancel();
        ctx.ErrorTcs.TrySetResult(new WebSocketDisconnect());

        try {
          await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, null, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
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
}
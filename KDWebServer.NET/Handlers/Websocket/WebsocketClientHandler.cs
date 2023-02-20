using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NLog;
using NLog.Fluent;
using WebSocketSharp;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;
using HttpListenerContext = WebSocketSharp.Net.HttpListenerContext;

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
      var wsCtx = _httpContext.AcceptWebSocket(null);

      var ws = wsCtx.WebSocket;
      WebsocketRequestContext ctx = new WebsocketRequestContext(_httpContext, RemoteEndpoint, Match, ws);

      var cts = new CancellationTokenSource();

      ws.OnMessage += (_, args) => {
        try {
          ctx.ReceiverQ.Enqueue(args, cts.Token);
        }
        catch (TaskCanceledException) {
        }
      };
      ws.OnClose += (_, args) => {
        cts.Cancel();
        ctx.ErrorTcs.TrySetResult(new WebSocketDisconnect(args));
      };
      ws.OnError += (_, args) => {
        ctx.ErrorTcs.TrySetResult(new WebSocketError(args));
      };

      var _ = Task.Run(() => {
        while (!cts.Token.IsCancellationRequested) {
          try {
            var msg = ctx.SenderQ.Dequeue(cts.Token);
            if (ws.ReadyState == WebSocketState.Open) {
              switch (msg) {
                case byte[] data:
                  ws.Send(data);
                  break;
                case string text:
                  ws.Send(text);
                  break;
              }
            }
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

        ws.Close(CloseStatusCode.Normal);

        Logger.Trace()
              .Message($"[{ClientId}] WS handler finished gracefully - {logSuffix}")
              .Properties(props)
              .Write();
      }
      catch (WebSocketDisconnect e) {
        Logger.Trace()
              .Message($"[{ClientId}] WS connection has been closed, code: {e.CloseEvent.Code}, reason: {e.CloseEvent.Reason}, clean: {e.CloseEvent.WasClean} - {logSuffix}")
              .Properties(props)
              .Write();
      }
      catch (Exception e) {
        Logger.Error()
              .Message($"[{ClientId}] Error during handling WS connection - {logSuffix}")
              .Properties(props)
              .Exception(e)
              .Write();

        ws.Close(CloseStatusCode.Abnormal);
      }
    }
  }

  [PublicAPI]
  public class WebSocketDisconnect : Exception
  {
    public CloseEventArgs CloseEvent { get; }

    public WebSocketDisconnect(CloseEventArgs closeEvent)
    {
      CloseEvent = closeEvent;
    }
  }

  [PublicAPI]
  public class WebSocketError : Exception
  {
    public ErrorEventArgs ErrorEvent { get; }

    public WebSocketError(ErrorEventArgs errorEvent)
    {
      ErrorEvent = errorEvent;
    }
  }
}
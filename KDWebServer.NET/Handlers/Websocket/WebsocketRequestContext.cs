using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Nito.AsyncEx;

namespace KDWebServer.Handlers.Websocket
{
  public class WebsocketMessage
  {
    public byte[] Data;
    public string Text;
  }

  [PublicAPI]
  public class WebsocketRequestContext
  {
    public readonly HttpListenerContext HttpContext;
    private readonly WebSocket _webSocket;

    public string Path => HttpContext.Request.Url.AbsolutePath;
    public string ForwardedUri => Headers.GetStringOrDefault("X-Forwarded-Uri", null);
    public System.Net.IPAddress RemoteEndpoint { get; }

    // Routing
    public Dictionary<string, object> Params { get; set; }

    // Params
    public QueryStringValuesCollection QueryString { get; }

    // Headers
    public QueryStringValuesCollection Headers { get; }

    // WebSocket
    internal readonly AsyncProducerConsumerQueue<object> SenderQ;

    internal readonly TaskCompletionSource<Exception> ErrorTcs = new();

    internal WebsocketRequestContext(HttpListenerContext httpContext,
                                     IPAddress remoteEndpoint,
                                     RequestDispatcher.RouteEndpointMatch match,
                                     WebSocket webSocket,
                                     int senderQueueLength)
    {
      _webSocket = webSocket;

      HttpContext = httpContext;

      Params = match.RouteMatch.Params;

      QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.QueryString);

      Headers = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Headers);

      RemoteEndpoint = remoteEndpoint;

      SenderQ = new(senderQueueLength);
    }

    public async Task<WebsocketMessage> ReceiveMessageAsync(CancellationToken token)
    {
      return await ReceiveMessage(_webSocket, token);
    }

    public async Task SendMessageAsync(byte[] data, CancellationToken token) => await SenderQ.EnqueueAsync(data, token);
    public async Task SendMessageAsync(string data, CancellationToken token) => await SenderQ.EnqueueAsync(data, token);
    public void SendMessageWait(byte[] data, CancellationToken token) => SenderQ.Enqueue(data, token);
    public void SendMessageWait(string data, CancellationToken token) => SenderQ.Enqueue(data, token);

    public async Task SendMessageAsync(byte[] data, TimeSpan timeout) => await SenderQ.EnqueueAsync(data, new CancellationTokenSource(timeout).Token);
    public async Task SendMessageAsync(string data, TimeSpan timeout) => await SenderQ.EnqueueAsync(data, new CancellationTokenSource(timeout).Token);
    public void SendMessageWait(byte[] data, TimeSpan timeout) => SenderQ.Enqueue(data, new CancellationTokenSource(timeout).Token);
    public void SendMessageWait(string data, TimeSpan timeout) => SenderQ.Enqueue(data, new CancellationTokenSource(timeout).Token);

    public async Task Close() => await Close(WebSocketCloseStatus.NormalClosure);
    public async Task Close(ushort code) => await Close(code, "");
    public async Task Close(WebSocketCloseStatus code) => await Close(code, "");
    public async Task Close(ushort code, string reason) => await Close((WebSocketCloseStatus)code, reason);
    public async Task Close(WebSocketCloseStatus code, string reason) => await _webSocket.CloseAsync(code, reason, CancellationToken.None);

    public void Abort() => _webSocket.Abort();

    private static async Task<WebsocketMessage> ReceiveMessage(WebSocket ws, CancellationToken token)
    {
      var buffer = new ArraySegment<byte>(new byte[8192]);

      WebSocketReceiveResult result;

      using var ms = new MemoryStream();

      do {
        result = await ws.ReceiveAsync(buffer, token);
        ms.Write(buffer.Array!, buffer.Offset, result.Count);
      } while (!result.EndOfMessage);

      ms.Seek(0, SeekOrigin.Begin);

      if (result.MessageType == WebSocketMessageType.Text) {
        using var reader = new StreamReader(ms, Encoding.UTF8);

        return new WebsocketMessage() {
            Text = await reader.ReadToEndAsync(),
        };
      }
      else {
        return new WebsocketMessage() {
            Data = ms.ToArray(),
        };
      }
    }
  }
}
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

  internal struct WebsocketOutgoingMessage
  {
    public ReadOnlyMemory<byte> Buffer;
    public WebSocketMessageType MessageType;
    public bool EndOfMessage;
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
    internal readonly AsyncProducerConsumerQueue<WebsocketOutgoingMessage> SenderQ;

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

    public void SendTextWait(string data, CancellationToken token) => Enqueue(data, true, true, token);
    public void SendTextWait(ReadOnlyMemory<byte> data, CancellationToken token) => Enqueue(data, true, true, token);
    public void SendTextWait(string data, TimeSpan timeout) => Enqueue(data, true, true, new CancellationTokenSource(timeout).Token);
    public void SendTextWait(ReadOnlyMemory<byte> data, TimeSpan timeout) => Enqueue(data, true, true, new CancellationTokenSource(timeout).Token);
    public async Task SendTextAsync(string data, CancellationToken token) => await EnqueueAsync(data, true, true, token);
    public async Task SendTextAsync(ReadOnlyMemory<byte> data, CancellationToken token) => await EnqueueAsync(data, true, true, token);
    public async Task SendTextAsync(string data, TimeSpan timeout) => await EnqueueAsync(data, true, true, new CancellationTokenSource(timeout).Token);
    public async Task SendTextAsync(ReadOnlyMemory<byte> data, TimeSpan timeout) => await EnqueueAsync(data, true, true, new CancellationTokenSource(timeout).Token);

    public void SendTextPartialWait(string data, CancellationToken token) => Enqueue(data, false, true, token);
    public void SendTextPartialWait(ReadOnlyMemory<byte> data, CancellationToken token) => Enqueue(data, false, true, token);
    public void SendTextPartialWait(string data, TimeSpan timeout) => Enqueue(data, false, true, new CancellationTokenSource(timeout).Token);
    public void SendTextPartialWait(ReadOnlyMemory<byte> data, TimeSpan timeout) => Enqueue(data, false, true, new CancellationTokenSource(timeout).Token);
    public async Task SendTextPartialAsync(string data, CancellationToken token) => await EnqueueAsync(data, false, true, token);
    public async Task SendTextPartialAsync(ReadOnlyMemory<byte> data, CancellationToken token) => await EnqueueAsync(data, false, true, token);
    public async Task SendTextPartialAsync(string data, TimeSpan timeout) => await EnqueueAsync(data, false, true, new CancellationTokenSource(timeout).Token);
    public async Task SendTextPartialAsync(ReadOnlyMemory<byte> data, TimeSpan timeout) => await EnqueueAsync(data, false, true, new CancellationTokenSource(timeout).Token);

    public void SendBinaryWait(ReadOnlyMemory<byte> data, CancellationToken token) => Enqueue(data, true, false, token);
    public void SendBinaryWait(ReadOnlyMemory<byte> data, TimeSpan timeout) => Enqueue(data, true, false, new CancellationTokenSource(timeout).Token);
    public async Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken token) => await EnqueueAsync(data, true, false, token);
    public async Task SendBinaryAsync(ReadOnlyMemory<byte> data, TimeSpan timeout) => await EnqueueAsync(data, true, false, new CancellationTokenSource(timeout).Token);

    public void SendBinaryPartialWait(ReadOnlyMemory<byte> data, CancellationToken token) => Enqueue(data, false, false, token);
    public void SendBinaryPartialWait(ReadOnlyMemory<byte> data, TimeSpan timeout) => Enqueue(data, false, false, new CancellationTokenSource(timeout).Token);
    public async Task SendBinaryPartialAsync(ReadOnlyMemory<byte> data, CancellationToken token) => await EnqueueAsync(data, false, false, token);
    public async Task SendBinaryPartialAsync(ReadOnlyMemory<byte> data, TimeSpan timeout) => await EnqueueAsync(data, false, false, new CancellationTokenSource(timeout).Token);

    private void Enqueue(string data, bool isEnd, bool isText, CancellationToken token)
    {
      Enqueue(Encoding.UTF8.GetBytes(data), isEnd, isText, token);
    }

    private void Enqueue(ReadOnlyMemory<byte> data, bool isEnd, bool isText, CancellationToken token)
    {
      SenderQ.Enqueue(new WebsocketOutgoingMessage() {
          Buffer = data,
          EndOfMessage = isEnd,
          MessageType = isText ? WebSocketMessageType.Text : WebSocketMessageType.Binary,
      }, token);
    }

    private async Task EnqueueAsync(string data, bool isEnd, bool isText, CancellationToken token)
    {
      await EnqueueAsync(Encoding.UTF8.GetBytes(data), isEnd, isText, token);
    }

    private async Task EnqueueAsync(ReadOnlyMemory<byte> data, bool isEnd, bool isText, CancellationToken token)
    {
      await SenderQ.EnqueueAsync(new WebsocketOutgoingMessage() {
          Buffer = data,
          EndOfMessage = isEnd,
          MessageType = isText ? WebSocketMessageType.Text : WebSocketMessageType.Binary,
      }, token);
    }

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
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Nito.AsyncEx;

namespace KDWebServer.Handlers.Websocket;

[PublicAPI]
public class WebsocketMessage
{
  public byte[]? Data;
  public string? Text;
}

internal struct WebsocketOutgoingMessage
{
  public ReadOnlyMemory<byte> Buffer;
  public WebSocketMessageType MessageType;
  public bool EndOfMessage;
  public Action? OnSent;
}

[PublicAPI]
public class WebsocketRequestContext : IRequestContext
{
  private readonly WebSocket _webSocket;

  public HttpListenerContext HttpContext { get; }
  public CancellationToken Token { get; }

  public string Path => HttpContext.Request.Url!.AbsolutePath;
  public string? ForwardedUri => Headers.TryGetString("X-Forwarded-Uri", out var value) ? value : null;
  public IPAddress RemoteEndpoint { get; }

  public HttpMethod HttpMethod => HttpMethod.Get;

  // Routing
  public Dictionary<string, object> Params { get; set; }

  // Params
  public QueryStringValuesCollection QueryString { get; }

  // Headers
  public QueryStringValuesCollection Headers { get; }

  // WebSocket
  internal readonly Channel<WebsocketOutgoingMessage> SenderQ;
  public int SenderQueueSize => SenderQ.Reader.Count;

  internal WebsocketRequestContext(HttpListenerContext httpContext,
                                   IPAddress remoteEndpoint,
                                   RequestDispatcher.RouteEndpointMatch match,
                                   WebSocket webSocket,
                                   int senderQueueLength,
                                   CancellationToken token)
  {
    _webSocket = webSocket;

    HttpContext = httpContext;
    Token = token;

    Params = match.RouteParams;

    QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.QueryString);

    Headers = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Headers);

    RemoteEndpoint = remoteEndpoint;

    SenderQ = Channel.CreateBounded<WebsocketOutgoingMessage>(senderQueueLength);
  }

  public async Task<WebsocketMessage> ReceiveMessageAsync(CancellationToken token)
  {
    return await ReceiveMessage(_webSocket, token);
  }

  public bool TrySendText(string data) => TryEnqueue(data, null, true, true);
  public bool TrySendText(ReadOnlyMemory<byte> data) => TryEnqueue(data, null, true, true);
  public async Task SendTextAsync(string data, CancellationToken token) => await EnqueueAsync(data, null, true, true, token);
  public async Task SendTextAsync(ReadOnlyMemory<byte> data, CancellationToken token) => await EnqueueAsync(data, null, true, true, token);

  public bool TrySendTextPartial(string data) => TryEnqueue(data, null, false, true);
  public bool TrySendTextPartial(ReadOnlyMemory<byte> data) => TryEnqueue(data, null, false, true);
  public async Task SendTextPartialAsync(string data, CancellationToken token) => await EnqueueAsync(data, null, false, true, token);
  public async Task SendTextPartialAsync(ReadOnlyMemory<byte> data, CancellationToken token) => await EnqueueAsync(data, null, false, true, token);

  public bool TrySendBinary(ReadOnlyMemory<byte> data) => TryEnqueue(data, null, true, false);
  public async Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken token) => await EnqueueAsync(data, null, true, false, token);

  public bool TrySendBinaryPartial(ReadOnlyMemory<byte> data) => TryEnqueue(data, null, false, false);
  public async Task SendBinaryPartialAsync(ReadOnlyMemory<byte> data, CancellationToken token) => await EnqueueAsync(data, null, false, false, token);

  public bool TryEnqueue(string data, Action? onSent, bool isEnd, bool isText)
  {
    return TryEnqueue(Encoding.UTF8.GetBytes(data), onSent, isEnd, isText);
  }

  public bool TryEnqueue(ReadOnlyMemory<byte> data, Action? onSent, bool isEnd, bool isText)
  {
    var msg = new WebsocketOutgoingMessage() {
        Buffer = data,
        EndOfMessage = isEnd,
        MessageType = isText ? WebSocketMessageType.Text : WebSocketMessageType.Binary,
        OnSent = onSent,
    };

    var res = SenderQ.Writer.TryWrite(msg);
    
    if (!res && SenderQ.Reader.Completion.IsCompleted) {
      throw new WebSocketDisconnect();
    }

    return res;
  }

  public async Task EnqueueAsync(string data, Action? onSent, bool isEnd, bool isText, CancellationToken token)
  {
    await EnqueueAsync(Encoding.UTF8.GetBytes(data), onSent, isEnd, isText, token);
  }

  public async Task EnqueueAsync(ReadOnlyMemory<byte> data, Action? onSent, bool isEnd, bool isText, CancellationToken token)
  {
    var msg = new WebsocketOutgoingMessage() {
        Buffer = data,
        EndOfMessage = isEnd,
        MessageType = isText ? WebSocketMessageType.Text : WebSocketMessageType.Binary,
        OnSent = onSent,
    };

    try {
      await SenderQ.Writer.WriteAsync(msg, token);
    }
    catch (ChannelClosedException) {
      throw new WebSocketDisconnect();
    }
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

    // ReSharper disable once ConvertIfStatementToSwitchStatement
    if (result.MessageType == WebSocketMessageType.Close) {
      throw new WebSocketDisconnect();
    }
    else if (result.MessageType == WebSocketMessageType.Text) {
      using var reader = new StreamReader(ms, Encoding.UTF8);

      return new WebsocketMessage() {
          // ReSharper disable once MethodHasAsyncOverload
          Text = reader.ReadToEnd(),
      };
    }
    else if (result.MessageType == WebSocketMessageType.Binary) {
      return new WebsocketMessage() {
          Data = ms.ToArray(),
      };
    }
    else {
      throw new InvalidOperationException();
    }
  }
}
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Nito.AsyncEx;
using WebSocketSharp;
using HttpListenerContext = WebSocketSharp.Net.HttpListenerContext;

namespace KDWebServer.Handlers.Websocket
{
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
    internal readonly AsyncProducerConsumerQueue<MessageEventArgs> ReceiverQ = new(1);

    // WebSocketSharp has no good support for real async operations so we are using queue approach
    internal readonly AsyncProducerConsumerQueue<object> SenderQ = new(1);

    internal readonly TaskCompletionSource<Exception> ErrorTcs = new();

    internal WebsocketRequestContext(HttpListenerContext httpContext, IPAddress remoteEndpoint, RequestDispatcher.RouteEndpointMatch match, WebSocket webSocket)
    {
      _webSocket = webSocket;

      HttpContext = httpContext;

      Params = match.RouteMatch.Params;

      QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.QueryString);

      Headers = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Headers);

      RemoteEndpoint = remoteEndpoint;
    }

    public async Task<MessageEventArgs> ReceiveMessageAsync()
    {
      var deqTask = ReceiverQ.DequeueAsync();
      var errTask = ErrorTcs.Task;
      var task = await Task.WhenAny(deqTask, errTask);

      if (task == deqTask) {
        return deqTask.Result;
      }
      else {
        throw errTask.Result;
      }
    }

    public async Task SendMessageAsync(byte[] data) => await SenderQ.EnqueueAsync(data);
    public async Task SendMessageAsync(string data) => await SenderQ.EnqueueAsync(data);
    public void SendMessageWait(byte[] data) => SenderQ.Enqueue(data);
    public void SendMessageWait(string data) => SenderQ.Enqueue(data);

    public void Close() => _webSocket.Close();
    public void Close(ushort code) => _webSocket.Close(code);
    public void Close(CloseStatusCode code) => _webSocket.Close(code);
    public void Close(ushort code, string reason) => _webSocket.Close(code, reason);
    public void Close(CloseStatusCode code, string reason) => _webSocket.Close(code, reason);
  }
}
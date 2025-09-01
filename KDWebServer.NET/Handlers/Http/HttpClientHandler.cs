using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nito.AsyncEx;
using Nito.Disposables;
using NLog;

namespace KDWebServer.Handlers.Http;

public class HttpClientHandler
{
  private static readonly JsonSerializerSettings JsonSerializerSettings = new() { DateParseHandling = DateParseHandling.None };

  private readonly HttpContext _httpContext;
  private readonly DateTime _connectionTime;
  private readonly Stopwatch _requestTimer;
  private readonly RequestDispatcher.RouteEndpointMatch Match;
  public long HandlerTime;
  public long ProcessingTime;

  private WebServer WebServer { get; }
  public ILogger Logger { get; }
  public ILogger LoggerResponse { get; }
  public string RawUrl { get; }
  public string ClientId { get; }
  private IPAddress RemoteEndpoint { get; }

  internal HttpClientHandler(WebServer webServer, HttpContext httpContext, IPAddress remoteEndpoint, string rawUrl, DateTime connectionTime, Stopwatch requestTimer, string clientId, RequestDispatcher.RouteEndpointMatch match)
  {
    _httpContext = httpContext;
    _connectionTime = connectionTime;
    _requestTimer = requestTimer;
    WebServer = webServer;
    Logger = webServer.LogFactory?.GetLogger("webserver.http") ?? LogManager.LogFactory.CreateNullLogger();
    LoggerResponse = webServer.LogFactory?.GetLogger("webserver.http.response") ?? LogManager.LogFactory.CreateNullLogger();

    RemoteEndpoint = remoteEndpoint;
    RawUrl = rawUrl;
    ClientId = clientId;
    Match = match;
  }

  public async Task Handle(Dictionary<string, object?> advLogProperties)
  {
    var serverShutdownToken = WebServer.ServerShutdownToken;

    _httpContext.Response.Headers.Add("Access-Control-Allow-Origin", "*");

    HttpRequestContext ctx;

    var requestAbortedToken = _httpContext.Response.HttpContext.RequestAborted;

    var props = new Dictionary<string, object?>(advLogProperties);
    props.Add("webserver.content_type", _httpContext.Request.ContentType);
    props.Add("webserver.content_length", _httpContext.Request.ContentLength);
    try {
      var rawData = await ReadPayload(_httpContext, serverShutdownToken).ConfigureAwait(false);

      ctx = new HttpRequestContext(_httpContext, RemoteEndpoint, RawUrl, Match, rawData, requestAbortedToken);

      if (_httpContext.Request.ContentType != null) {
        var parsedContent = ProcessKnownTypes(ctx);
        props.Add("content", WebServer.Config.Logger.LogPayloads ? parsedContent : "<skipped>");
      }
    }
    catch (Exception e) {
      Logger.ForInfoEvent()
            .Message($"[{ClientId}] Error during reading/parsing HTTP request - {_httpContext.Request.Method} {_httpContext.Request.Path.Value} - {e.Message}")
            .Properties(props)
            .Property("webserver.status_code", 400)
            .Log();

      Helpers.SetResponse(_httpContext.Response, 400);
      return;
    }

    var ep = Match.Endpoint;

    Logger.ForInfoEvent()
          .Message($"[{ClientId}] New HTTP request - {_httpContext.Request.Method} {_httpContext.Request.Path.Value}")
          .Properties(props)
          .Property("webserver.time_conn", $"{(int)(_connectionTime - DateTime.UtcNow).TotalMilliseconds}ms")
          .Log();

    Stopwatch timer = new Stopwatch();
    timer.Start();
    try {
      using var requestCancellationCts = CancellationTokenSource.CreateLinkedTokenSource(requestAbortedToken, serverShutdownToken);

      WebServerResponse response;
      try {
        // ReSharper disable AccessToDisposedClosure
        if (ep.RunOnThreadPool) {
          response = await Task.Run(async () => await ep.HttpCallback!(ctx, requestCancellationCts.Token).ConfigureAwait(false), requestCancellationCts.Token).ConfigureAwait(false);
        }
        else if (WebServer.SynchronizationContext == null) {
          response = await ep.HttpCallback!(ctx, requestCancellationCts.Token).ConfigureAwait(false);
        }
        else {
          var scope = ScopeContext.GetAllProperties().ToArray();
          response = await WebServer.SynchronizationContext.PostAsync(async () => {
            using var _ = ScopeContext.PushProperties(scope);
            return await ep.HttpCallback!(ctx, requestCancellationCts.Token).ConfigureAwait(false);
          }).ConfigureAwait(false);
        }
        // ReSharper restore AccessToDisposedClosure
      }
      catch (WebServerResponse r) {
        response = r;
      }

      HandlerTime = timer.ElapsedMilliseconds;
      ProcessingTime = _requestTimer.ElapsedMilliseconds;

      foreach (string responseHeader in response.Headers)
        _httpContext.Response.Headers.Add(responseHeader, response.Headers[responseHeader]);

      foreach (var observer in WebServer.Observers)
        observer.AfterRequestCallback(_httpContext, Match, response, timer.Elapsed, _requestTimer.Elapsed);

      await response.WriteToResponse(this, _httpContext.Response, WebServer.Config.Logger, advLogProperties, requestAbortedToken).ConfigureAwait(false);

      foreach (var observer in WebServer.Observers)
        observer.AfterRequestSent(_httpContext, Match, response, _requestTimer.Elapsed);
    }
    catch (OperationCanceledException) when (serverShutdownToken.IsCancellationRequested) {
      Helpers.SetResponse(_httpContext.Response, 444, "server is being shut down");
    }
    catch (OperationCanceledException) when (requestAbortedToken.IsCancellationRequested) {
      Logger.ForInfoEvent()
            .Message($"[{ClientId}] Request aborted - {_httpContext.Request.Method} {_httpContext.Request.Path.Value}")
            .Properties(props)
            .Log();
    }
    catch (HttpListenerException e) when (e.ErrorCode == -2146232800) { // Unable to write data to the transport connection: Broken pipe.
      // transport is already closed
    }
    catch (HttpListenerException e) when (e is { ErrorCode: 64, NativeErrorCode: 64 }) { // The specified network name is no longer available.
      // transport is already closed
    }
    catch (Exception e) {
      ProcessingTime = _requestTimer.ElapsedMilliseconds;

      Logger.ForErrorEvent()
            .Message($"[{ClientId}] Error during handling HTTP request ({ProcessingTime}ms) - {_httpContext.Request.Method} {_httpContext.Request.Path.Value}")
            .Properties(props)
            .Property("webserver.status_code", 500)
            .Exception(e)
            .Log();

      Helpers.SetResponse(_httpContext.Response, 500);
    }
  }

  private static async Task<byte[]> ReadPayload(HttpContext httpContext, CancellationToken token)
  {
    // if (!httpContext.Request.HasEntityBody)
    //   return Array.Empty<byte>();

    using var ms = new MemoryStream();

    await httpContext.Request.Body.CopyToAsync(ms, token);

    return ms.ToArray();
  }

  private static object ProcessKnownTypes(HttpRequestContext ctx)
  {
    ContentType ct;

    var httpContext = ctx.HttpContext;

    if (httpContext.Request.ContentType == null)
      return "(no-type)";

    try {
      ct = new ContentType(httpContext.Request.ContentType);
    }
    catch (FormatException) {
      return "(invalid-type)";
    }

    string payload;

    switch (ct.MediaType) {
      case "application/x-www-form-urlencoded":
        // if (!httpContext.Request.HasFormContentType)
        //   return "(empty)";

        payload = Encoding.UTF8.GetString(ctx.RawData);

        ctx.FormData = QueryStringValuesCollection.Parse(payload);
        return ctx.FormData;

      case "application/json":
        // if (!httpContext.Request.HasFormContentType)
        //   return "(empty)";

        payload = Encoding.UTF8.GetString(ctx.RawData);

        ctx.JsonData = JsonConvert.DeserializeObject<JToken>(payload, JsonSerializerSettings)!;
        return ctx.JsonData;

      case "text/xml":
        // if (!httpContext.Request.HasFormContentType)
        //   return "(empty)";

        payload = Encoding.UTF8.GetString(ctx.RawData);

        ctx.XmlData = XDocument.Parse(payload);
        return ctx.XmlData;

      default:
        return "(unknown-type)";
    }
  }
}
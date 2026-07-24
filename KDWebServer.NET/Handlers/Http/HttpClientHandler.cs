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
using NLog;

namespace KDWebServer.Handlers.Http;

public class HttpClientHandler
{
  private static readonly JsonSerializerSettings JsonSerializerSettings = new() { DateParseHandling = DateParseHandling.None };

  private ILogger Logger { get; }
  internal ILogger LoggerResponse { get; }

  private WebServer WebServer { get; }
  private readonly InternalRequestContext _ictx;

  public string ClientId => _ictx.ClientId;

  internal long HandlerTime;
  internal long ProcessingTime;

  internal HttpClientHandler(WebServer webServer, InternalRequestContext ictx)
  {
    _ictx = ictx;
    WebServer = webServer;
    Logger = webServer.LogFactory?.GetLogger("webserver.http") ?? LogManager.LogFactory.CreateNullLogger();
    LoggerResponse = webServer.LogFactory?.GetLogger("webserver.http.response") ?? LogManager.LogFactory.CreateNullLogger();
  }

  public async Task Handle(Dictionary<string, object?> advLogProperties)
  {
    using var requestCancellationCts = CancellationTokenSource.CreateLinkedTokenSource(_ictx.HttpContext.Response.HttpContext.RequestAborted, WebServer.ServerShutdownToken);
    var requestAbortedToken = requestCancellationCts.Token;

    HttpRequestContext ctx;

    var props = new Dictionary<string, object?>(advLogProperties);
    props.Add("webserver.content_type", _ictx.HttpContext.Request.ContentType);
    props.Add("webserver.content_length", _ictx.HttpContext.Request.ContentLength);
    try {
      var rawData = await ReadPayload(_ictx.HttpContext, requestAbortedToken).ConfigureAwait(false);

      ctx = new HttpRequestContext(_ictx, rawData, requestAbortedToken);

      if (_ictx.HttpContext.Request.ContentType != null) {
        var parsedContent = ProcessKnownTypes(ctx);
        props.Add("content", WebServer.Config.Logger.LogPayloads ? parsedContent : "<skipped>");
      }
    }
    catch (Exception e) {
      Logger.ForInfoEvent()
            .Message($"[{_ictx.ClientId}] Error during reading/parsing HTTP request - {_ictx.HttpContext.Request.Method} {_ictx.HttpContext.Request.Path.Value} - {e.Message}")
            .Properties(props)
            .Property("webserver.status_code", 400)
            .Log();

      Helpers.SetResponse(_ictx.HttpContext.Response, 400);
      return;
    }

    var ep = _ictx.Match.Endpoint;

    Logger.ForInfoEvent()
          .Message($"[{_ictx.ClientId}] New HTTP request - {_ictx.HttpContext.Request.Method} {_ictx.HttpContext.Request.Path.Value}")
          .Properties(props)
          .Property("webserver.time_conn", $"{(int)(_ictx.ConnectionTime - DateTime.UtcNow).TotalMilliseconds}ms")
          .Log();

    Stopwatch timer = new Stopwatch();
    timer.Start();
    try {
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
      ProcessingTime = _ictx.RequestTimer.ElapsedMilliseconds;

      foreach (string responseHeader in response.Headers)
        _ictx.HttpContext.Response.Headers.Add(responseHeader, response.Headers[responseHeader]);

      foreach (var observer in WebServer.Observers)
        observer.AfterRequestCallback(_ictx.HttpContext, _ictx.Match, response, timer.Elapsed, _ictx.RequestTimer.Elapsed);

      await response.WriteToResponse(this, _ictx.HttpContext.Response, WebServer.Config.Logger, advLogProperties, requestAbortedToken).ConfigureAwait(false);

      foreach (var observer in WebServer.Observers)
        observer.AfterRequestSent(_ictx.HttpContext, _ictx.Match, response, _ictx.RequestTimer.Elapsed);
    }
    catch (OperationCanceledException) when (WebServer.ServerShutdownToken.IsCancellationRequested) {
      Helpers.SetResponse(_ictx.HttpContext.Response, 444, "server is being shut down");
    }
    catch (OperationCanceledException) when (requestAbortedToken.IsCancellationRequested) {
      Logger.ForInfoEvent()
            .Message($"[{_ictx.ClientId}] Request aborted - {_ictx.HttpContext.Request.Method} {_ictx.HttpContext.Request.Path.Value}")
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
      ProcessingTime = _ictx.RequestTimer.ElapsedMilliseconds;

      Logger.ForErrorEvent()
            .Message($"[{_ictx.ClientId}] Error during handling HTTP request ({ProcessingTime}ms) - {_ictx.HttpContext.Request.Method} {_ictx.HttpContext.Request.Path.Value}")
            .Properties(props)
            .Property("webserver.status_code", 500)
            .Exception(e)
            .Log();

      Helpers.SetResponse(_ictx.HttpContext.Response, 500);
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
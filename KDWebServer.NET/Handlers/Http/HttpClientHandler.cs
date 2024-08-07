﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nito.AsyncEx;
using NLog;

namespace KDWebServer.Handlers.Http;

public class HttpClientHandler
{
  private static readonly JsonSerializerSettings JsonSerializerSettings = new() { DateParseHandling = DateParseHandling.None };

  private readonly HttpListenerContext _httpContext;
  private readonly DateTime _connectionTime;
  private readonly Stopwatch _requestTimer;
  private readonly RequestDispatcher.RouteEndpointMatch Match;
  public long HandlerTime;
  public long ProcessingTime;

  private WebServer WebServer { get; }
  public ILogger Logger { get; }
  public string ClientId { get; }
  private IPAddress RemoteEndpoint { get; }

  internal HttpClientHandler(WebServer webServer, HttpListenerContext httpContext, IPAddress remoteEndpoint, DateTime connectionTime, Stopwatch requestTimer, string clientId, RequestDispatcher.RouteEndpointMatch match)
  {
    _httpContext = httpContext;
    _connectionTime = connectionTime;
    _requestTimer = requestTimer;
    WebServer = webServer;
    Logger = webServer.LogFactory?.GetLogger("webserver.http") ?? LogManager.LogFactory.CreateNullLogger();

    RemoteEndpoint = remoteEndpoint;
    ClientId = clientId;
    Match = match;
  }

  public async Task Handle(Dictionary<string, object?> advLogProperties)
  {
    var serverShutdownToken = WebServer.ServerShutdownToken;

    _httpContext.Response.AppendHeader("Access-Control-Allow-Origin", "*");

    HttpRequestContext ctx;

    var props = new Dictionary<string, object?>(advLogProperties);
    props.Add("webserver.content_type", _httpContext.Request.ContentType);
    props.Add("webserver.content_length", _httpContext.Request.ContentLength64);
    try {
      var rawData = await ReadPayload(_httpContext, serverShutdownToken).ConfigureAwait(false);

      ctx = new HttpRequestContext(_httpContext, RemoteEndpoint, Match, rawData, serverShutdownToken);

      if (_httpContext.Request.ContentType != null) {
        var parsedContent = ProcessKnownTypes(ctx);
        props.Add("content", WebServer.LoggerConfig.LogPayloads ? parsedContent : "<skipped>");
      }
    }
    catch (Exception e) {
      Logger.ForInfoEvent()
            .Message($"[{ClientId}] Error during reading/parsing HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url!.AbsolutePath} - {e.Message}")
            .Properties(props)
            .Property("webserver.status_code", 400)
            .Log();

      Helpers.SetResponse(_httpContext.Response, 400);
      return;
    }

    var ep = Match.Endpoint;

    Logger.ForInfoEvent()
          .Message($"[{ClientId}] New HTTP request - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url!.AbsolutePath}")
          .Properties(props)
          .Property("webserver.time_conn", $"{(int)(_connectionTime - DateTime.UtcNow).TotalMilliseconds}ms")
          .Log();

    Stopwatch timer = new Stopwatch();
    timer.Start();
    try {
      WebServerResponse response;
      try {
        if (ep.RunOnThreadPool) {
          response = await Task.Run(async () => await ep.HttpCallback!(ctx).ConfigureAwait(false), serverShutdownToken).ConfigureAwait(false);
        }
        else if (WebServer.SynchronizationContext == null) {
          response = await ep.HttpCallback!(ctx).ConfigureAwait(false);
        }
        else {
          var scope = ScopeContext.GetAllProperties().ToArray();
          response = await WebServer.SynchronizationContext.PostAsync(async () => {
            using var _ = ScopeContext.PushProperties(scope);
            return await ep.HttpCallback!(ctx).ConfigureAwait(false);
          }).ConfigureAwait(false);
        }
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

      await response.WriteToResponse(this, _httpContext.Response, WebServer.LoggerConfig, advLogProperties);

      foreach (var observer in WebServer.Observers)
        observer.AfterRequestSent(_httpContext, Match, response, _requestTimer.Elapsed);
    }
    catch (OperationCanceledException) {
      Helpers.SetResponse(_httpContext.Response, 444, "server is being shut down");
    }
    catch (HttpListenerException e) when (e.ErrorCode == -2146232800) { // Unable to write data to the transport connection: Broken pipe.
      // transport is already closed
    }
    catch (Exception e) {
      ProcessingTime = _requestTimer.ElapsedMilliseconds;

      Logger.ForErrorEvent()
            .Message($"[{ClientId}] Error during handling HTTP request ({ProcessingTime}ms) - {_httpContext.Request.HttpMethod} {_httpContext.Request.Url.AbsolutePath}")
            .Properties(props)
            .Property("webserver.status_code", 500)
            .Exception(e)
            .Log();

      Helpers.SetResponse(_httpContext.Response, 500);
    }
  }

  private static async Task<byte[]> ReadPayload(HttpListenerContext httpContext, CancellationToken token)
  {
    if (!httpContext.Request.HasEntityBody)
      return Array.Empty<byte>();

    using var ms = new MemoryStream();

    await httpContext.Request.InputStream.CopyToAsync(ms, token);

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
        if (!httpContext.Request.HasEntityBody)
          return "(empty)";

        payload = httpContext.Request.ContentEncoding.GetString(ctx.RawData);

        ctx.FormData = QueryStringValuesCollection.Parse(payload);
        return ctx.FormData;

      case "application/json":
        if (!httpContext.Request.HasEntityBody)
          return "(empty)";

        payload = httpContext.Request.ContentEncoding.GetString(ctx.RawData);

        ctx.JsonData = JsonConvert.DeserializeObject<JToken>(payload, JsonSerializerSettings)!;
        return ctx.JsonData;

      case "text/xml":
        if (!httpContext.Request.HasEntityBody)
          return "(empty)";

        payload = httpContext.Request.ContentEncoding.GetString(ctx.RawData);

        ctx.XmlData = XDocument.Parse(payload);
        return ctx.XmlData;

      default:
        return "(unknown-type)";
    }
  }
}
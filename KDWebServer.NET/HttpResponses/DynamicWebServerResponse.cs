﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class DynamicWebServerResponse : WebServerResponse
{
  private readonly string _mimeType;
  private readonly Func<Stream, Task> _builder;

  internal DynamicWebServerResponse(string mimeType, Func<Stream, Task> builder)
  {
    _mimeType = mimeType;
    _builder = builder;
  }

  public override async Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                             Dictionary<string, object?> loggingProps)
  {
    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] starting dynamic response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms)")
           .Properties(loggingProps)
           .Property("webserver.status_code", StatusCode)
           .Log();

    response.StatusCode = StatusCode;
    response.SendChunked = true;
    response.ContentType = _mimeType;

    var s = Stopwatch.StartNew();
    
    await _builder(response.OutputStream);

    try {
      await response.OutputStream.FlushAsync();
    }
    catch (ObjectDisposedException) {
      // stream may be already closed by the user, which is fine
    }

    var duration = s.ElapsedMilliseconds;

    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] finished dynamic response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms, dynamic: {duration}ms)")
           .Properties(loggingProps)
           .Property("webserver.status_code", StatusCode)
           .Log();
  }
}
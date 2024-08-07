﻿using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class StreamWebServerResponse : WebServerResponse
{
  private readonly Stream _stream;
  private readonly bool _closeAfter;
  private readonly string _mimeType;

  internal StreamWebServerResponse(Stream stream, bool closeAfter, string mimeType = "application/octet-stream")
  {
    _stream = stream;
    _closeAfter = closeAfter;
    _mimeType = mimeType;
  }

  public override async Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                             Dictionary<string, object?> loggingProps)
  {
    long lengthToSend = -1;
    var lengthToSendStr = "unknown length";
    if (_stream.CanSeek) {
      var curPos = _stream.Position;
      var endPos = _stream.Seek(0, SeekOrigin.End);
      _stream.Seek(curPos, SeekOrigin.Begin);
      lengthToSend = endPos - curPos;
      lengthToSendStr = WebServerUtils.BytesToString(lengthToSend);
    }

    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] sending stream response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({lengthToSendStr})")
           .Properties(loggingProps)
           .Property("webserver.status_code", StatusCode)
           .Log();

    response.StatusCode = StatusCode;
    response.ContentType = _mimeType;

    if (lengthToSend == -1) {
      response.SendChunked = true;
    }
    else {
      response.ContentLength64 = lengthToSend;
    }

    await _stream.CopyToAsync(response.OutputStream);
    if (_closeAfter)
      _stream.Close();
  }
}
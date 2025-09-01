using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using Microsoft.AspNetCore.Http;
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

  public override async Task WriteToResponse(HttpClientHandler handler, HttpResponse response, WebServerLoggerConfig loggerConfig,
                                             Dictionary<string, object?> loggingProps, CancellationToken token)
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

    handler.LoggerResponse.ForInfoEvent()
           .Message($"[{handler.ClientId}] sending stream response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({lengthToSendStr})")
           .Properties(loggingProps)
           .Property("webserver.status_code", StatusCode)
           .Log();

    response.StatusCode = StatusCode;
    response.ContentType = _mimeType;

    var s = Stopwatch.StartNew();

    if (lengthToSend == -1) {
      // response.SendChunked = true;
    }
    else {
      response.ContentLength = lengthToSend;
    }

    await _stream.CopyToAsync(response.Body, token);
    if (_closeAfter)
      _stream.Close();
    
    var duration = s.ElapsedMilliseconds;

    handler.LoggerResponse.ForInfoEvent()
           .Message($"[{handler.ClientId}] finished stream response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms, dynamic: {duration}ms)")
           .Properties(loggingProps)
           .Property("webserver.status_code", StatusCode)
           .Log();
  }
}
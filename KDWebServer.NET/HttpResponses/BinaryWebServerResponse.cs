using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class BinaryWebServerResponse : WebServerResponse
{
  private readonly byte[] _data;
  private readonly string _mimeType;

  internal BinaryWebServerResponse(byte[] data, string mimeType = "application/octet-stream")
  {
    _data = data;
    _mimeType = mimeType;
  }

  public override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                       Dictionary<string, object?> loggingProps)
  {
    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] sending binary response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({WebServerUtils.BytesToString(_data.Length)})")
           .Properties(loggingProps)
           .Property("webserver.data_length", _data.Length)
           .Property("webserver.status_code", StatusCode)
           .Log();

    response.StatusCode = StatusCode;
    response.ContentType = _mimeType;
    response.ContentLength64 = _data.LongLength;

    return response.OutputStream.WriteAsync(_data, 0, _data.Length);
  }
}
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class TextWebServerResponse : WebServerResponse
{
  private readonly string _text;
  private readonly string _contentType;

  internal TextWebServerResponse(string text, string contentType = "text/plain")
  {
    _text = text;
    _contentType = contentType;
  }

  internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                         Dictionary<string, object?> loggingProps)
  {
    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] sending text response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({WebServerUtils.LimitText(_text, 30).Replace("\n", " ")})")
           .Properties(loggingProps)
           .Property("text", loggerConfig.LogPayloads ? WebServerUtils.LimitText(_text, 1000) : "<skipped>")
           .Property("status_code", StatusCode)
           .Log();

    byte[] resp = Encoding.UTF8.GetBytes(_text);

    response.StatusCode = StatusCode;
    response.SendChunked = true;
    response.ContentType = _contentType;
    response.ContentLength64 = resp.LongLength;

    return response.OutputStream.WriteAsync(resp, 0, resp.Length);
  }
}
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class HtmlWebServerResponse : WebServerResponse
{
  private readonly string _html;

  internal HtmlWebServerResponse(string html)
  {
    _html = html;
  }

  internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                         Dictionary<string, object?> loggingProps)
  {
    var text = Utils.ExtractSimpleHtmlText(_html);

    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] sending HTML response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({Utils.LimitText(text, 30).Replace("\n", " ")})")
           .Properties(loggingProps)
           .Property("body", loggerConfig.LogPayloads ? Utils.LimitText(text, 1000) : "<skipped>")
           .Property("status_code", StatusCode)
           .Log();

    byte[] resp = Encoding.UTF8.GetBytes(_html);

    response.StatusCode = StatusCode;
    response.SendChunked = true;
    response.ContentType = "text/html";
    response.ContentLength64 = resp.LongLength;

    return response.OutputStream.WriteAsync(resp, 0, resp.Length);
  }
}
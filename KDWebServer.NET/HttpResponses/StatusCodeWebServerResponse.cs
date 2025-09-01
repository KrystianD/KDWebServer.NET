using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using Microsoft.AspNetCore.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class StatusCodeWebServerResponse : WebServerResponse
{
  private readonly string _text;

  internal StatusCodeWebServerResponse(int code, string text = "")
  {
    _text = text;
    StatusCode = code;
  }

  internal StatusCodeWebServerResponse(HttpStatusCode code, string text = "") : this((int)code, text)
  {
  }

  public override Task WriteToResponse(HttpClientHandler handler, HttpResponse response, WebServerLoggerConfig loggerConfig,
                                       Dictionary<string, object?> loggingProps, CancellationToken token)
  {
    HttpStatusCode code = (HttpStatusCode)StatusCode;

    var textStr = "";
    if (_text != "")
      textStr = $" with text: /{WebServerUtils.LimitText(_text, 30)}/";

    handler.LoggerResponse.ForInfoEvent()
           .Message($"[{handler.ClientId}] sending {code} code response{textStr} ({handler.HandlerTime}ms,{handler.ProcessingTime}ms)")
           .Properties(loggingProps)
           .Property("webserver.text", _text)
           .Property("webserver.status_code", StatusCode)
           .Log();

    response.StatusCode = StatusCode;
    if (_text == "") {
      return Task.CompletedTask;
    }
    else {
      byte[] resp = Encoding.UTF8.GetBytes(_text);
      response.ContentType = "text/plain";
      response.ContentLength = resp.LongLength;

      return response.Body.WriteAsync(resp, 0, resp.Length, token);
    }
  }
}
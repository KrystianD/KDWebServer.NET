using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace KDWebServer.HttpResponses;

public class NotFoundWebServerResponse : WebServerResponse
{
  private readonly string? _text;
  private readonly string? _json;
  private readonly string? _html;

  internal NotFoundWebServerResponse(string? text = null, object? json = null, string? html = null)
  {
    _text = text;
    _json = json == null ? null : JToken.FromObject(json).ToString(Formatting.None);
    _html = html;

    if ((_text != null ? 1 : 0) + (_json != null ? 1 : 0) + (_html != null ? 1 : 0) > 1) {
      throw new ArgumentException("at most one text, json and html arguments can be set");
    }

    StatusCode = 404;
  }

  internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                         Dictionary<string, object?> loggingProps)
  {
    var logMsg = handler.Logger.ForTraceEvent()
                        .Property("status_code", StatusCode);

    byte[]? resp = null;
    if (_text != null) {
      logMsg.Message($"[{handler.ClientId}] sending NotFound response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({Utils.LimitText(_text, 30).Replace("\n", " ")})")
            .Properties(loggingProps)
            .Property("text", Utils.LimitText(_text, 1000));

      resp = Encoding.UTF8.GetBytes(_text);
      response.ContentType = "text/plain";
    }
    else if (_json != null) {
      logMsg.Message($"[{handler.ClientId}] sending NotFound response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms)")
            .Properties(loggingProps)
            .Property("data", Utils.LimitText(_json, 1000));

      resp = Encoding.UTF8.GetBytes(_json);
      response.ContentType = "application/json";
    }
    else if (_html != null) {
      var text = Utils.ExtractSimpleHtmlText(_html);

      logMsg.Message($"[{handler.ClientId}] sending NotFound response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({Utils.LimitText(text, 30).Replace("\n", " ")})")
            .Properties(loggingProps)
            .Property("body", Utils.LimitText(text, 1000));

      resp = Encoding.UTF8.GetBytes(_html);
      response.ContentType = "text/html";
    }

    logMsg.Log();

    response.StatusCode = StatusCode;
    if (resp != null) {
      response.SendChunked = true;
      response.ContentLength64 = resp.LongLength;
      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }
    else {
      return Task.CompletedTask;
    }
  }
}
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace KDWebServer.HttpResponses;

public class JsonWebServerResponse : WebServerResponse
{
  private readonly string _json;

  internal JsonWebServerResponse(object data, bool indented)
  {
    _json = JToken.FromObject(data).ToString(indented ? Formatting.Indented : Formatting.None);
  }

  public override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                       Dictionary<string, object?> loggingProps)
  {
    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] sending JSON response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms)")
           .Properties(loggingProps)
           .Property("webserver.data", loggerConfig.LogPayloads ? WebServerUtils.LimitText(_json, 1000) : "<skipped>")
           .Property("webserver.status_code", StatusCode)
           .Log();

    byte[] resp = Encoding.UTF8.GetBytes(_json);

    response.StatusCode = StatusCode;
    response.ContentType = "application/json";
    response.ContentLength64 = resp.LongLength;

    return response.OutputStream.WriteAsync(resp, 0, resp.Length);
  }
}
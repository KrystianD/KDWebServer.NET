using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class XmlWebServerResponse : WebServerResponse
{
  private readonly string _xml;

  internal XmlWebServerResponse(string xml)
  {
    _xml = xml;
  }

  public override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                       Dictionary<string, object?> loggingProps)
  {
    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] sending XML response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({WebServerUtils.LimitText(_xml, 30).Replace("\n", " ")})")
           .Properties(loggingProps)
           .Property("webserver.xml", loggerConfig.LogPayloads ? WebServerUtils.LimitText(_xml, 1000) : "<skipped>")
           .Property("webserver.status_code", StatusCode)
           .Log();

    byte[] resp = Encoding.UTF8.GetBytes(_xml);

    response.StatusCode = StatusCode;
    response.ContentType = "text/xml";
    response.ContentLength64 = resp.LongLength;

    return response.OutputStream.WriteAsync(resp, 0, resp.Length);
  }
}
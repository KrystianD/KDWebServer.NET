using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using Microsoft.AspNetCore.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class XmlWebServerResponse : WebServerResponse
{
  private readonly string _xml;

  internal XmlWebServerResponse(string xml)
  {
    _xml = xml;
  }

  public override Task WriteToResponse(HttpClientHandler handler, HttpResponse response, WebServerLoggerConfig loggerConfig,
                                       Dictionary<string, object?> loggingProps, CancellationToken token)
  {
    handler.LoggerResponse.ForInfoEvent()
           .Message($"[{handler.ClientId}] sending XML response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({WebServerUtils.LimitText(_xml, 30).Replace("\n", " ")})")
           .Properties(loggingProps)
           .Property("webserver.xml", loggerConfig.LogPayloads ? WebServerUtils.LimitText(_xml, 1000) : "<skipped>")
           .Property("webserver.status_code", StatusCode)
           .Log();

    byte[] resp = Encoding.UTF8.GetBytes(_xml);

    response.StatusCode = StatusCode;
    response.ContentType = "text/xml";
    response.ContentLength = resp.LongLength;

    return response.Body.WriteAsync(resp, 0, resp.Length, token);
  }
}
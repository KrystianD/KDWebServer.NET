using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog.Fluent;

namespace KDWebServer.HttpResponses;

public class XMLWebServerResponse : IWebServerResponse
{
  private readonly string _xml;

  private XMLWebServerResponse(string xml)
  {
    _xml = xml;
  }

  internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                         Dictionary<string, object?> loggingProps)
  {
    handler.Logger.Trace()
           .Message($"[{handler.ClientId}] sending XML response ({handler.ProcessingTime}ms) ({Utils.LimitText(_xml, 30).Replace("\n", " ")})")
           .Properties(loggingProps)
           .Property("xml", loggerConfig.LogPayloads ? Utils.LimitText(_xml, 1000) : "<skipped>")
           .Property("status_code", StatusCode)
           .Write();

    byte[] resp = Encoding.UTF8.GetBytes(_xml);

    response.StatusCode = StatusCode;
    response.SendChunked = true;
    response.ContentType = "text/xml";
    response.ContentLength64 = resp.LongLength;

    return response.OutputStream.WriteAsync(resp, 0, resp.Length);
  }

  internal static XMLWebServerResponse FromString(string xml) => new(xml);
}
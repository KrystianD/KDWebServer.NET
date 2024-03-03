using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog;

namespace KDWebServer.HttpResponses;

public class RedirectWebServerResponse : WebServerResponse
{
  private readonly string _location;

  internal RedirectWebServerResponse(string location)
  {
    StatusCode = 302;
    _location = location;
  }

  internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                         Dictionary<string, object?> loggingProps)
  {
    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] sending Redirect response ({handler.ProcessingTime}ms) (to {_location})")
           .Properties(loggingProps)
           .Property("location", _location)
           .Property("status_code", StatusCode)
           .Log();

    response.StatusCode = StatusCode;
    response.RedirectLocation = _location;

    return Task.CompletedTask;
  }
}
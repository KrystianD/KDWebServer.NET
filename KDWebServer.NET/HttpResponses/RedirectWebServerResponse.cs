using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using Microsoft.AspNetCore.Http;
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

  public override Task WriteToResponse(HttpClientHandler handler, HttpResponse response, WebServerLoggerConfig loggerConfig,
                                       Dictionary<string, object?> loggingProps, CancellationToken token)
  {
    handler.LoggerResponse.ForInfoEvent()
           .Message($"[{handler.ClientId}] sending Redirect response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) (to {_location})")
           .Properties(loggingProps)
           .Property("webserver.location", _location)
           .Property("webserver.status_code", StatusCode)
           .Log();

    response.StatusCode = StatusCode;
    response.Redirect( _location);

    return Task.CompletedTask;
  }
}
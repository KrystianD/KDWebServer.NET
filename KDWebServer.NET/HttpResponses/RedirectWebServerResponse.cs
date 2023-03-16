using System.Net;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog.Fluent;

namespace KDWebServer.HttpResponses
{
  public class RedirectWebServerResponse : IWebServerResponse
  {
    private readonly string _location;

    private RedirectWebServerResponse(string location)
    {
      StatusCode = 302;
      _location = location;
    }

    internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig)
    {
      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending Redirect response ({handler.ProcessingTime}ms) (to {_location})")
             .Property("location", _location)
             .Property("status_code", StatusCode)
             .Write();

      response.StatusCode = StatusCode;
      response.RedirectLocation = _location;

      return Task.CompletedTask;
    }

    internal static RedirectWebServerResponse FromLocation(string location) => new RedirectWebServerResponse(location);
  }
}
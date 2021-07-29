using WebSocketSharp.Net;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class RedirectWebServerResponse : IWebServerResponse
  {
    private readonly string _location;

    private RedirectWebServerResponse(string location)
    {
      StatusCode = 302;
      _location = location;
    }

    internal override Task WriteToResponse(WebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending Redirect response ({handler.ProcessingTime}ms) (to {_location})")
             .Property("location", _location)
             .Property("code", StatusCode)
             .Write();

      response.StatusCode = StatusCode;
      response.RedirectLocation = _location;

      return Task.CompletedTask;
    }

    internal static RedirectWebServerResponse FromLocation(string location) => new RedirectWebServerResponse(location);
  }
}
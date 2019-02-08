using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer
{
  public class RedirectWebServerResponse : IWebServerResponse
  {
    private readonly string _location;

    public RedirectWebServerResponse(string location)
    {
      _location = location;
    }

    public override Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending Redirect response ({handler.ProcessingTime}ms) (to {_location})")
             .Property("location", _location)
             .Property("client_id", handler.ClientId)
             .Write();

      response.StatusCode = 302;
      response.RedirectLocation = _location;

      return Task.CompletedTask;
    }
  }
}
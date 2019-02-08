using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer
{
  public class NotFoundWebServerResponse : IWebServerResponse
  {
    public override Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message("[{webServer.clientId}] sending NotFound response ({webServer.ProcessingTime}ms)")
             .Property("client_id", handler.ClientId)
             .Write();

      response.StatusCode = 404;
      return Task.CompletedTask;
    }
  }
}
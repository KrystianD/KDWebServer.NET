using WebSocketSharp.Net;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class NotFoundWebServerResponse : IWebServerResponse
  {
    public NotFoundWebServerResponse()
    {
      StatusCode = 404;
    }

    internal override Task WriteToResponse(WebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message("[{webServer.clientId}] sending NotFound response ({webServer.ProcessingTime}ms)")
             .Property("code", StatusCode)
             .Property("client_id", handler.ClientId)
             .Write();

      response.StatusCode = StatusCode;
      return Task.CompletedTask;
    }

    internal static NotFoundWebServerResponse Create() => new NotFoundWebServerResponse();
  }
}
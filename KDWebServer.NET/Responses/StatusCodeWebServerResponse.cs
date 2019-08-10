using System.Net;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class StatusCodeWebServerResponse : IWebServerResponse
  {
    private StatusCodeWebServerResponse(int code)
    {
      StatusCode = code;
    }

    internal override Task WriteToResponse(WebServerClientHandler handler, HttpListenerResponse response)
    {
      HttpStatusCode code = (HttpStatusCode) StatusCode;

      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending {code} code response ({handler.ProcessingTime}ms)")
             .Property("code", StatusCode)
             .Property("client_id", handler.ClientId)
             .Write();

      response.StatusCode = StatusCode;
      return Task.CompletedTask;
    }

    internal static StatusCodeWebServerResponse FromStatusCode(int statusCode) => new StatusCodeWebServerResponse(statusCode);
    internal static StatusCodeWebServerResponse FromStatusCode(HttpStatusCode statusCode) => FromStatusCode((int) statusCode);
  }
}
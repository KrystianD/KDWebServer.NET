using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer
{
  public class ErrorCodeWebServerResponse : IWebServerResponse
  {
    private readonly int _errorCode;

    public ErrorCodeWebServerResponse(int errorCode)
    {
      _errorCode = errorCode;
    }

    public ErrorCodeWebServerResponse(HttpStatusCode code)
    {
      _errorCode = (int) code;
    }

    public override Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending {_errorCode} code response ({handler.ProcessingTime}ms)")
             .Property("code", _errorCode)
             .Property("client_id", handler.ClientId)
             .Write();

      response.StatusCode = _errorCode;
      return Task.CompletedTask;
    }
  }
}
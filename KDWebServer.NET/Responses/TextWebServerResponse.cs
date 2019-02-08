using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer
{
  public class TextWebServerResponse : IWebServerResponse
  {
    private readonly string _text;
    private readonly string _contentType;

    public TextWebServerResponse(string text, string contentType = "text/plain")
    {
      _text = text;
      _contentType = contentType;
    }

    public override Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending text response ({handler.ProcessingTime}ms) ({Utils.LimitText(_text, 30)})")
             .Property("text", _text)
             .Property("client_id", handler.ClientId)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(_text);

      response.StatusCode = _statusCode;
      response.SendChunked = true;
      response.ContentType = _contentType;
      response.ContentLength64 = resp.LongLength;

      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }
  }
}
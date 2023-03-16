using System.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog.Fluent;

namespace KDWebServer.HttpResponses
{
  public class TextWebServerResponse : IWebServerResponse
  {
    private readonly string _text;
    private readonly string _contentType;

    private TextWebServerResponse(string text, string contentType)
    {
      _text = text;
      _contentType = contentType;
    }

    internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig)
    {
      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending text response ({handler.ProcessingTime}ms) ({Utils.LimitText(_text, 30).Replace("\n", " ")})")
             .Property("text", loggerConfig.LogPayloads ? Utils.LimitText(_text, 1000) : "<skipped>")
             .Property("status_code", StatusCode)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(_text);

      response.StatusCode = StatusCode;
      response.SendChunked = true;
      response.ContentType = _contentType;
      response.ContentLength64 = resp.LongLength;

      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }

    internal static TextWebServerResponse FromString(string text, string contentType = "text/plain") => new TextWebServerResponse(text, contentType);
  }
}
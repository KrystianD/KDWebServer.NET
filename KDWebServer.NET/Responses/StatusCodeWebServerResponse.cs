using WebSocketSharp.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class StatusCodeWebServerResponse : IWebServerResponse
  {
    private readonly string _text;

    private StatusCodeWebServerResponse(int code, string text = "")
    {
      _text = text;
      StatusCode = code;
    }

    internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response)
    {
      HttpStatusCode code = (HttpStatusCode)StatusCode;

      var textStr = "";
      if (_text != "")
        textStr = $" with text: /{Utils.LimitText(_text, 30)}/";

      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending {code} code response{textStr} ({handler.ProcessingTime}ms)")
             .Property("text", _text)
             .Property("status_code", StatusCode)
             .Write();

      response.StatusCode = StatusCode;
      if (_text == null) {
        return Task.CompletedTask;
      }
      else {
        byte[] resp = Encoding.UTF8.GetBytes(_text);
        response.SendChunked = true;
        response.ContentType = "text/plain";
        response.ContentLength64 = resp.LongLength;

        return response.OutputStream.WriteAsync(resp, 0, resp.Length);
      }
    }


    internal static StatusCodeWebServerResponse FromStatusCode(int statusCode, string text = "") => new StatusCodeWebServerResponse(statusCode, text);
    internal static StatusCodeWebServerResponse FromStatusCode(System.Net.HttpStatusCode statusCode, string text = "") => FromStatusCode((int)statusCode, text);
  }
}
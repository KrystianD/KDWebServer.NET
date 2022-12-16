using WebSocketSharp.Net;
using System.Text;
using System.Threading.Tasks;
using KDWebServer.Handlers;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class HTMLWebServerResponse : IWebServerResponse
  {
    private readonly string _html;

    private HTMLWebServerResponse(string html)
    {
      _html = html;
    }

    internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response)
    {
      var text = Utils.ExtractSimpleHtmlText(_html);

      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending HTML response ({handler.ProcessingTime}ms) ({Utils.LimitText(text, 30).Replace("\n", " ")})")
             .Property("body", Utils.LimitText(text, 1000))
             .Property("status_code", StatusCode)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(_html);

      response.StatusCode = StatusCode;
      response.SendChunked = true;
      response.ContentType = "text/html";
      response.ContentLength64 = resp.LongLength;

      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }

    internal static HTMLWebServerResponse FromString(string html) => new HTMLWebServerResponse(html);
  }
}
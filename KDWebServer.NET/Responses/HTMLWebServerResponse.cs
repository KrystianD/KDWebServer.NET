using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer
{
  public class HTMLWebServerResponse : IWebServerResponse
  {
    private readonly string _html;

    public HTMLWebServerResponse(string html)
    {
      _html = html;
    }

    public override Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response)
    {
      var text = Utils.ExtractSimpleHtmlText(_html, 1000);

      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending HTML response ({handler.ProcessingTime}ms) ({Utils.LimitText(text, 100).Replace("\n", " ")})")
             .Property("body", text)
             .Property("client_id", handler.ClientId)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(_html);

      response.StatusCode = _statusCode;
      response.SendChunked = true;
      response.ContentType = "text/html";
      response.ContentLength64 = resp.LongLength;

      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }
  }
}
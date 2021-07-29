using WebSocketSharp.Net;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class XMLWebServerResponse : IWebServerResponse
  {
    private readonly string _xml;

    private XMLWebServerResponse(string xml)
    {
      _xml = xml;
    }

    internal override Task WriteToResponse(WebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending XML response ({handler.ProcessingTime}ms) ({Utils.LimitText(_xml, 100).Replace("\n", " ")})")
             .Property("xml", _xml)
             .Property("code", StatusCode)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(_xml);

      response.StatusCode = StatusCode;
      response.SendChunked = true;
      response.ContentType = "text/xml";
      response.ContentLength64 = resp.LongLength;

      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }

    internal static XMLWebServerResponse FromString(string xml) => new XMLWebServerResponse(xml);
  }
}
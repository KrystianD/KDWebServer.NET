using System.Net;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer
{
  public class XMLWebServerResponse : IWebServerResponse
  {
    private readonly string _xml;

    public XMLWebServerResponse(string xml)
    {
      _xml = xml;
    }

    public override Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending XML response ({handler.ProcessingTime}ms) ({Utils.LimitText(_xml, 100).Replace("\n", " ")})")
             .Property("xml", _xml)
             .Property("client_id", handler.ClientId)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(_xml);

      response.StatusCode = _statusCode;
      response.SendChunked = true;
      response.ContentType = "text/xml";
      response.ContentLength64 = resp.LongLength;

      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }
  }
}
using System.Net;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;

namespace KDWebServer
{
  public abstract class IWebServerResponse : System.Exception
  {
    public int StatusCode { get; set; } = 200;

    internal readonly WebHeaderCollection _headers = new WebHeaderCollection();

    internal abstract Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig);

    public void SetHeader(HttpResponseHeader header, string value) => _headers.Add(header, value);
    public void SetHeader(string name, string value) => _headers.Add(name, value);
  }
}
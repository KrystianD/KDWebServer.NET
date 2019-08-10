using System.Net;
using System.Threading.Tasks;

namespace KDWebServer
{
  public abstract class IWebServerResponse
  {
    public int StatusCode { get; set; } = 200;
    
    internal readonly WebHeaderCollection _headers = new WebHeaderCollection();

    internal abstract Task WriteToResponse(WebServerClientHandler handler, HttpListenerResponse response);

    public void SetHeader(HttpResponseHeader header, string value) => _headers.Add(header, value);
    public void SetHeader(string name, string value) => _headers.Add(name, value);
  }
}
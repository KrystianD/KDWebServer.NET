using System.Net;
using System.Threading.Tasks;

namespace KDWebServer
{
  public abstract class IWebServerResponse
  {
    internal int _statusCode = 200;
    internal WebHeaderCollection _headers = new WebHeaderCollection();

    public abstract Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response);

    public void SetStatusCode(int statusCode) => _statusCode = statusCode;
    
    public void SetHeader(HttpResponseHeader header, string value) => _headers.Add(header, value);
    public void SetHeader(string name, string value) => _headers.Add(name, value);
  }
}
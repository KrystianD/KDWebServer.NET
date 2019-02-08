using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace KDWebServer
{
  public class WebServerRequestContext
  {
    public HttpListenerContext httpContext;

    public string Path => httpContext.Request.Url.AbsolutePath;
    public string HttpMethod => httpContext.Request.HttpMethod;

    private string requestPayload;

    // Routing
    public Dictionary<string, object> Params { get; set; }

    // Params
    public QueryStringValuesCollection QueryString { get; }

    // Request
    public string ForwardedUri { get; }
    public IPAddress RemoteEndpoint { get; }

    // Headers
    public QueryStringValuesCollection Headers { get; }

    // Content
    public QueryStringValuesCollection FormData { get; set; }
    public JToken JsonData { get; set; }
    public XDocument XmlData { get; set; }

    public WebServerRequestContext(HttpListenerContext httpContext)
    {
      this.httpContext = httpContext;

      QueryString = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.QueryString);
      Headers = QueryStringValuesCollection.FromNameValueCollection(httpContext.Request.Headers);

      RemoteEndpoint = Utils.GetClientIp(httpContext);
      ForwardedUri = Headers.GetStringOrDefault("X-Forwarded-Uri", null);
    }

    public async Task<string> ReadAsString()
    {
      if (!httpContext.Request.HasEntityBody)
        return null;
      using (Stream inputStream = httpContext.Request.InputStream) {
        using (StreamReader streamReader = new StreamReader(inputStream, httpContext.Request.ContentEncoding)) {
          requestPayload = await streamReader.ReadToEndAsync();
          return requestPayload;
        }
      }
    }
  }
}
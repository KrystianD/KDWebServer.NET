using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using JetBrains.Annotations;
using KDWebServer.Handlers.Http;

namespace KDWebServer;

[PublicAPI]
public abstract class IWebServerResponse : System.Exception
{
  public int StatusCode { get; set; } = 200;

  internal readonly WebHeaderCollection Headers = new();

  internal abstract Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                         Dictionary<string, object?> loggingProps);

  public void SetHeader(HttpResponseHeader header, string value) => Headers.Add(header, value);
  public void SetHeader(string name, string value) => Headers.Add(name, value);
}
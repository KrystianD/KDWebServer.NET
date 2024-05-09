using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using JetBrains.Annotations;

namespace KDWebServer.Handlers;

[PublicAPI]
public interface IRequestContext
{
  public HttpListenerContext HttpContext { get; }
  public CancellationToken Token { get; }

  public string Path { get; }
  public string? ForwardedUri { get; }
  public IPAddress RemoteEndpoint { get; }

  public HttpMethod HttpMethod { get; }

  // Routing
  public Dictionary<string, object> Params { get; set; }

  // Params
  public QueryStringValuesCollection QueryString { get; }

  // Headers
  public QueryStringValuesCollection Headers { get; }
}
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using JetBrains.Annotations;
using KDWebServer.Auth;
using Microsoft.AspNetCore.Http;

namespace KDWebServer.Handlers;

[PublicAPI]
public interface IRequestContext
{
  public HttpContext HttpContext { get; }
  public CancellationToken Token { get; }

  public string Path { get; }
  public IPAddress RemoteEndpoint { get; }
  public string RawUrl { get; }

  public HttpMethod HttpMethod { get; }

  // Routing
  public Dictionary<string, object> Params { get; set; }

  // Params
  public QueryStringValuesCollection QueryString { get; }

  // Auth
  public IAuthState AuthState { get; }
}
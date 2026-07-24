using System;
using System.Diagnostics;
using System.Net;
using KDWebServer.Auth;
using Microsoft.AspNetCore.Http;

namespace KDWebServer.Handlers;

internal class InternalRequestContext
{
  public HttpContext HttpContext;
  public IPAddress RemoteEndpoint;
  public string RawUrl;
  public string ClientId;
  public DateTime ConnectionTime;
  public Stopwatch RequestTimer;
  public RequestDispatcher.RouteEndpointMatch Match;
  public IAuthState AuthState = NotAuthenticatedAuthState.Instance;
}
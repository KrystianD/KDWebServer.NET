using System.Threading.Tasks;
using JetBrains.Annotations;
using KDWebServer.Auth;

namespace KDWebServer;

[PublicAPI]
public class WebServerConfig
{
  public WebServerRouterConfig Router = new();
  public WebServerLoggerConfig Logger = new();
  public WebServerAuthConfig Auth = new();
  public bool CORSAllowAll;
}

[PublicAPI]
public class WebServerRouterConfig
{
  public bool AllowTrailingSlash = false;
  public bool AllowDuplicatedSlashes = false;
}

[PublicAPI]
public class WebServerLoggerConfig
{
  public bool LogPayloads = true;
}

[PublicAPI]
public class WebServerAuthConfig
{
  public bool BearerAuthEnabled = false;

  public BearerAuthHandler BearerAuthHandler = _ => ValueTask.FromResult<IAuthState>(NotAuthenticatedAuthState.Instance);
}
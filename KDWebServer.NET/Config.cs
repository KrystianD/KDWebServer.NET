using JetBrains.Annotations;

namespace KDWebServer;

[PublicAPI]
public class WebServerConfig
{
  public WebServerRouterConfig Router = new();
  public WebServerLoggerConfig Logger = new();
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
using System;

namespace KDWebServer
{
  public class RouteInvalidValueProvidedException : Exception
  {
    public RouteInvalidValueProvidedException(string name, string type, string value)
        : base($"Error during parsing path parameter: /{name}/, expected type: /{type}/, got value of: /{value}/")
    {
    }
  }
}
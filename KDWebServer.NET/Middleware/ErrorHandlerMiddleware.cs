using System;

namespace KDWebServer.Middleware;

public abstract class ErrorHandlerMiddleware
{
  public abstract WebServerResponse Process(Exception exception);
}
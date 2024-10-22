using System;
using JetBrains.Annotations;
using KDWebServer.Middleware;

namespace KDWebServer.ClassHandler.Attributes;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class ErrorHandlerMiddlewareAttribute : Attribute
{
  public Func<ErrorHandlerMiddleware> Factory { get; }

  public ErrorHandlerMiddlewareAttribute(Type factory)
  {
    Factory = () => (ErrorHandlerMiddleware)Activator.CreateInstance(factory)!;
  }
}
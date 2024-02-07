using System;
using System.Net.Http;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes.Methods;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class PostAttribute : EndpointAttribute
{
  public PostAttribute(string endpoint) : base(endpoint, HttpMethod.Post)
  {
  }
}
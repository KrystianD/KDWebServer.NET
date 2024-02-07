using System;
using System.Net.Http;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes.Methods;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class GetAttribute : EndpointAttribute
{
  public GetAttribute(string endpoint) : base(endpoint, HttpMethod.Get)
  {
  }
}
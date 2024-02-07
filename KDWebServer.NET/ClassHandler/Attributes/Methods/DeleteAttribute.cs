using System;
using System.Net.Http;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes.Methods;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class DeleteAttribute : EndpointAttribute
{
  public DeleteAttribute(string endpoint) : base(endpoint, HttpMethod.Delete)
  {
  }
}
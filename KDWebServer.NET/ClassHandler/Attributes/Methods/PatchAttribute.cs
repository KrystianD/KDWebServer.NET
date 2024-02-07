using System;
using System.Net.Http;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes.Methods;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class PatchAttribute : EndpointAttribute
{
  public PatchAttribute(string endpoint) : base(endpoint, HttpMethod.Patch)
  {
  }
}
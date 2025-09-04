using System;
using System.Net.Http;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes.Methods;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class PutAttribute : EndpointAttribute
{
  public PutAttribute(string endpoint) : base(endpoint, HttpMethod.Put)
  {
  }
}
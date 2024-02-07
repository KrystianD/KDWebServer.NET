using System;
using System.Net.Http;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class EndpointAttribute : Attribute
{
  public string Endpoint { get; }
  public HttpMethod HttpMethod { get; }

  public EndpointAttribute(string endpoint, HttpMethod httpMethod)
  {
    Endpoint = endpoint;
    HttpMethod = httpMethod;
  }
}
using System.Collections.Generic;
using NSwag;

namespace KDWebServer.ClassHandler.Creator;

internal class HandlerDescriptor
{
  public readonly List<MethodDescriptor> Methods = new();
  public readonly TypeSchemaRegistry TypeSchemaRegistry;
  public readonly OpenApiDocument OpenApiDocument = new();

  public HandlerDescriptor()
  {
    TypeSchemaRegistry = new(OpenApiDocument);
  }
}
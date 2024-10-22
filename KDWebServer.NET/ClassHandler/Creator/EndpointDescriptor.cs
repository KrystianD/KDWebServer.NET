using System.Collections.Generic;
using KDWebServer.ClassHandler.Attributes;
using NJsonSchema;
using NSwag;

namespace KDWebServer.ClassHandler.Creator;

internal record EndpointDescriptor(
    EndpointDefinition Definition,
    OpenApiDocument OpenApiDocument,
    TypeSchemaRegistry TypeSchemaRegistry,
    List<MethodParameterDescriptor> MethodParameterDescriptors,
    string RouterPath,
    JsonSchema? BodyJsonSchema,
    ResponseTypeEnum MethodResponseType);
using System.Collections.Generic;
using System.Reflection;
using KDWebServer.ClassHandler.Attributes;
using NJsonSchema;

namespace KDWebServer.ClassHandler.Creator;

internal record MethodDescriptor(
    MethodInfo MethodInfo,
    EndpointAttribute EndpointAttribute,
    List<MethodParameterDescriptor> MethodParameterDescriptors,
    string RouterPath,
    JsonSchema? BodyJsonSchema,
    ResponseTypeEnum MethodResponseType);
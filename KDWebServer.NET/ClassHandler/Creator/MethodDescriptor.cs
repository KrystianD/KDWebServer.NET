using System;
using System.Collections.Generic;
using KDWebServer.ClassHandler.Attributes;
using NJsonSchema;

namespace KDWebServer.ClassHandler.Creator;

internal record MethodDescriptor(
    Func<object?[], object?> Callable,
    List<MethodParameterDescriptor> MethodParameterDescriptors,
    JsonSchema? BodyJsonSchema,
    ResponseTypeEnum MethodResponseType);
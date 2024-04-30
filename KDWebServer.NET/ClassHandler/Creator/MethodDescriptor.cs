using System;
using System.Collections.Generic;
using KDWebServer.ClassHandler.Attributes;
using NJsonSchema;

namespace KDWebServer.ClassHandler.Creator;

internal record MethodDescriptor(
    HandlerDescriptor HandlerDescriptor,
    List<MethodParameterDescriptor> MethodParameterDescriptors,
    string RouterPath,
    JsonSchema? BodyJsonSchema,
    ResponseTypeEnum MethodResponseType);
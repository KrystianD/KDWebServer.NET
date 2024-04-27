using System;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class RunOnThreadPoolAttribute : Attribute
{
}
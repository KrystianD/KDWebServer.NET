using System;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes;

[PublicAPI]
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class HideFromExampleAttribute : Attribute
{
}
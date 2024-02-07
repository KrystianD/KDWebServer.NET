using System;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes;

[PublicAPI]
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ExampleAttribute : Attribute
{
  public object Value { get; }

  public ExampleAttribute(object value)
  {
    Value = value;
  }
}
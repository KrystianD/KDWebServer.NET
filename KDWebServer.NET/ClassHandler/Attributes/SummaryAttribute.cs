using System;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class SummaryAttribute : Attribute
{
  public string Value { get; }

  public SummaryAttribute(string value)
  {
    Value = value;
  }
}
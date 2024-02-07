using System;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes;

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class ReturnDescriptionAttribute : Attribute
{
  public string Description { get; }

  public ReturnDescriptionAttribute(string description)
  {
    Description = description;
  }
}
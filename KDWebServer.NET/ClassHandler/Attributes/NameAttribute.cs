using System;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes;

[PublicAPI]
[AttributeUsage(AttributeTargets.Parameter)]
public class NameAttribute : Attribute
{
  public string Name { get; internal set; }

  public NameAttribute(string name)
  {
    Name = name;
  }
}
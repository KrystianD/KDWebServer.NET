using System;
using JetBrains.Annotations;

namespace KDWebServer.ClassHandler.Attributes;

public enum ResponseTypeEnum
{
  Json,
  Text,
}

[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public class ResponseTypeAttribute : Attribute
{
  public ResponseTypeEnum Type { get; }

  public ResponseTypeAttribute(ResponseTypeEnum type)
  {
    Type = type;
  }
}
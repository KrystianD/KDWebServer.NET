using System;

namespace KDWebServer.ClassHandler.Exceptions;

internal class UnsupportedDataTypeException : Exception
{
  public UnsupportedDataTypeException(Type type) : base($"Type /{type}/ not supported")
  {
  }

  public UnsupportedDataTypeException(string message) : base(message)
  {
  }
}
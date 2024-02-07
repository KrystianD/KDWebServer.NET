using System;

namespace KDWebServer.ClassHandler.Exceptions;

internal class MethodDescriptorException : Exception
{
  public MethodDescriptorException(string message) : base(message)
  {
  }
}
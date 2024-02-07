using System;

namespace KDWebServer.ClassHandler.Creator;

internal class MethodParameterDescriptor
{
  public readonly string Name;
  public readonly Type ValueType;
  public readonly DefaultValue DefaultValue;
  public readonly string Description;

  public ParameterType? Type;

  // for Path
  public SimpleTypeConverters.TypeConverter? PathTypeConverter;

  // for Query
  public SimpleTypeConverters.TypeConverter? QueryTypeConverter;

  public MethodParameterDescriptor(string name, Type valueType, DefaultValue defaultValue, string description)
  {
    Name = name;
    ValueType = valueType;
    DefaultValue = defaultValue;
    Description = description;
  }
}
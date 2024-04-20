using System;

namespace KDWebServer.ClassHandler.Creator;

internal class MethodParameterDescriptor
{
  public readonly string Name;

  public readonly Type ValueType;
  public readonly bool IsNullable;
  public readonly DefaultValue DefaultValue;
  public readonly string Description;

  public ParameterKind? Kind;

  // for Path
  public SimpleTypeConverters.TypeConverter? PathTypeConverter;

  // for Query
  public SimpleTypeConverters.TypeConverter? QueryTypeConverter;
  public bool? QueryIsNullable;

  public MethodParameterDescriptor(string name, Type valueType, bool isNullable, DefaultValue defaultValue, string description)
  {
    Name = name;
    ValueType = valueType;
    IsNullable = isNullable;
    DefaultValue = defaultValue;
    Description = description;
  }
}
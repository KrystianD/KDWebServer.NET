using System;

namespace KDWebServer.ClassHandler.Creator;

internal class MethodParameterDescriptor
{
  public readonly string Name;

  public readonly Type ValueType;
  public readonly bool IsNullable;
  public readonly MethodParameterBuilder ParameterBuilder;

  public ParameterKind? Kind;

  // for Path
  public SimpleTypeConverters.TypeConverter? PathTypeConverter;

  // for Query
  public SimpleTypeConverters.TypeConverter? QueryTypeConverter;
  public bool? QueryIsNullable;

  public MethodParameterDescriptor(string name, Type valueType, bool isNullable, MethodParameterBuilder parameterBuilder)
  {
    Name = name;
    ValueType = valueType;
    IsNullable = isNullable;
    ParameterBuilder = parameterBuilder;
  }
}
using System;
using System.Reflection;

namespace KDWebServer.ClassHandler.Creator;

internal class MethodParameterDescriptor
{
  public readonly string Name;
  public readonly ParameterInfo ParameterInfo;
  public readonly Type ValueType;
  public readonly DefaultValue DefaultValue;
  public readonly string Description;

  public ParameterKind? Kind;

  // for Path
  public SimpleTypeConverters.TypeConverter? PathTypeConverter;

  // for Query
  public SimpleTypeConverters.TypeConverter? QueryTypeConverter;
  public bool? QueryIsNullable;

  public MethodParameterDescriptor(string name, ParameterInfo parameterInfo, Type valueType, DefaultValue defaultValue, string description)
  {
    Name = name;
    ParameterInfo = parameterInfo;
    ValueType = valueType;
    DefaultValue = defaultValue;
    Description = description;
  }
}
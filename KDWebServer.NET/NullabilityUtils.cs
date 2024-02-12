using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace KDWebServer;

public static class NullabilityUtils
{
  public static bool IsNullable(Type type, out Type innerType) =>
      IsNullableHelper(type, null, Array.Empty<CustomAttributeData>(), out innerType);

  public static bool IsNullable(PropertyInfo property, out Type innerType) =>
      IsNullableHelper(property.PropertyType, property.DeclaringType, property.CustomAttributes, out innerType);

  public static bool IsNullable(FieldInfo field, out Type innerType) =>
      IsNullableHelper(field.FieldType, field.DeclaringType, field.CustomAttributes, out innerType);

  public static bool IsNullable(ParameterInfo parameter, out Type innerType) =>
      IsNullableHelper(parameter.ParameterType, parameter.Member, parameter.CustomAttributes, out innerType);

  public static bool IsNullable(MemberInfo memberInfo, out Type innerType)
  {
    if (memberInfo is PropertyInfo propertyInfo) {
      return IsNullable(propertyInfo, out innerType);
    }
    else if (memberInfo is FieldInfo fieldInfo) {
      return IsNullable(fieldInfo, out innerType);
    }
    else {
      throw new ArgumentException("memberInfo must be an instance of PropertyInfo or FieldInfo", nameof(memberInfo));
    }
  }

  private static bool IsNullableHelper(Type memberType, MemberInfo? declaringType, IEnumerable<CustomAttributeData> customAttributes,
                                       [NotNullWhen(true)] out Type innerType)
  {
    if (memberType.IsValueType) {
      var underlyingType = Nullable.GetUnderlyingType(memberType);
      if (underlyingType != null) {
        innerType = underlyingType;
        return true;
      }
      else {
        innerType = memberType;
        return false;
      }
    }

    innerType = memberType;

    const string NullableAttribute = "System.Runtime.CompilerServices.NullableAttribute";
    const string NullableContextAttribute = "System.Runtime.CompilerServices.NullableContextAttribute";

    var nullable = customAttributes.FirstOrDefault(x => x.AttributeType.FullName == NullableAttribute);
    if (nullable is { ConstructorArguments.Count: 1 }) {
      var attributeArgument = nullable.ConstructorArguments[0];
      if (attributeArgument.ArgumentType == typeof(byte[])) {
        var args = (ReadOnlyCollection<CustomAttributeTypedArgument>)attributeArgument.Value!;
        if (args.Count > 0 && args[0].ArgumentType == typeof(byte)) {
          return (byte)args[0].Value! == 2;
        }
      }
      else if (attributeArgument.ArgumentType == typeof(byte)) {
        return (byte)attributeArgument.Value! == 2;
      }
    }

    for (var type = declaringType; type != null; type = type.DeclaringType) {
      var context = type.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == NullableContextAttribute);
      if (context is { ConstructorArguments.Count: 1 } &&
          context.ConstructorArguments[0].ArgumentType == typeof(byte)) {
        return (byte)context.ConstructorArguments[0].Value! == 2;
      }
    }

    return false;
  }
}
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NJsonSchema;

namespace KDWebServer;

public static class SimpleTypeConverters
{
  public class TypeConverter
  {
    public readonly Type Type;
    public readonly string RouterTypeName;
    public readonly Action<JsonSchema> ApplyToJsonSchema;
    public readonly Func<string, object> FromStringConverter;

    public TypeConverter(Type type, string routerTypeName, Action<JsonSchema> applyToJsonSchema, Func<string, object> fromStringConverter)
    {
      Type = type;
      RouterTypeName = routerTypeName;
      FromStringConverter = fromStringConverter;
      ApplyToJsonSchema = applyToJsonSchema;
    }
  }

  private static readonly List<TypeConverter> Converters = new() {
      new(typeof(object), "object",
          x => {
            x.Type = JsonObjectType.Object;
          },
          str => str),
      new(typeof(bool), "bool",
          x => {
            x.Type = JsonObjectType.Boolean;
          },
          str => bool.Parse(str)),
      new(typeof(string), "string",
          x => {
            x.Type = JsonObjectType.String;
          },
          str => str),
      new(typeof(int), "int",
          x => {
            x.Type = JsonObjectType.Number;
            x.Format = "int32";
          },
          str => int.Parse(str)),
      new(typeof(long), "long",
          x => {
            x.Type = JsonObjectType.Number;
            x.Format = "int64";
          },
          str => long.Parse(str)),
      new(typeof(Guid), "guid",
          x => {
            x.Type = JsonObjectType.String;
            x.Format = "uuid";
          },
          str => Guid.Parse(str)),
      new(typeof(decimal), "decimal",
          x => {
            x.Type = JsonObjectType.Number;
            x.Format = "decimal";
          },
          str => decimal.Parse(str)),
      new(typeof(float), "float",
          x => {
            x.Type = JsonObjectType.Number;
            x.Format = "float";
          },
          str => float.Parse(str)),
      new(typeof(double), "double",
          x => {
            x.Type = JsonObjectType.Number;
            x.Format = "double";
          },
          str => double.Parse(str)),
  };

  public static TypeConverter? GetConverterByType(Type type)
  {
    if (type.IsEnum) {
      var enumValues = Enum.GetValues(type);

      var enumStrs = new Dictionary<string, Enum>();
      for (int i = 0; i < enumValues.Length; i++) {
        var value = (Enum)enumValues.GetValue(i)!;

        FieldInfo fieldInfo = value.GetType().GetField(value.ToString())!;
        var attribute = (EnumMemberAttribute)fieldInfo.GetCustomAttribute(typeof(EnumMemberAttribute))!;
        if (attribute?.Value is null)
          throw new ArgumentException("All enum items must have EnumMember attribute set and not null");

        enumStrs.Add(attribute.Value, value);
      }

      return new TypeConverter(
          type,
          "string",
          schema => {
            schema.Type = JsonObjectType.String;
            foreach (var enumStr in enumStrs)
              schema.Enumeration.Add(enumStr.Key);
          },
          s => enumStrs[s]);
    }
    else if (NullabilityUtils.IsNullable(type, out var innerType)) {
      var innerTypeConverter = GetConverterByType(innerType);
      if (innerTypeConverter == null)
        return null;

      return new TypeConverter(innerTypeConverter.Type,
                               innerTypeConverter.RouterTypeName,
                               x => {
                                 innerTypeConverter.ApplyToJsonSchema(x);
                                 x.IsNullableRaw = true;
                               },
                               x => {
                                 if (x == null!)
                                   return null!;
                                 else
                                   return innerTypeConverter.FromStringConverter(x);
                               });
    }

    return Converters.Find(x => x.Type == type);
  }

  public static TypeConverter? GetConverterByRouterTypeName(string routerTypeName)
  {
    return Converters.Find(x => x.RouterTypeName == routerTypeName);
  }
}
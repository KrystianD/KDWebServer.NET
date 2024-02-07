using System;
using System.Collections.Generic;
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
  };

  public static TypeConverter? GetConverterByType(Type type)
  {
    return Converters.Find(x => x.Type == type);
  }

  public static TypeConverter? GetConverterByRouterTypeName(string routerTypeName)
  {
    return Converters.Find(x => x.RouterTypeName == routerTypeName);
  }
}
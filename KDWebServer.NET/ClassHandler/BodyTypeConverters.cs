using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NJsonSchema;

namespace KDWebServer.ClassHandler;

public static class BodyTypeConverters
{
  public class TypeConverter
  {
    public readonly Type Type;
    public readonly Action<JsonSchema> ApplyToJsonSchema;

    public TypeConverter(Type type, Action<JsonSchema> applyToJsonSchema)
    {
      Type = type;
      ApplyToJsonSchema = applyToJsonSchema;
    }
  }

  private static readonly List<TypeConverter> Converters = new() {
      new(typeof(object), x => {
        x.AllowAdditionalProperties = true;
        x.Description = "Can be any JSON";
        x.Example = "{}";
      }),
      new(typeof(JToken), x => {
        x.AllowAdditionalProperties = true;
        x.Description = "Can be any JSON token";
        x.Example = "\"data\"";
      }),
      new(typeof(JArray), x => {
        x.AllowAdditionalProperties = false;
        x.Description = "Can be any JSON array";
        x.Example = "[]";
      }),
      new(typeof(JObject), x => {
        x.AllowAdditionalProperties = true;
        x.Description = "Can be any JSON object";
        x.Example = "{}";
      }),
      new(typeof(JValue), x => {
        x.AllowAdditionalProperties = false;
        x.Description = "Can be any JSON value";
        x.Example = "\"data\"";
      }),
  };

  public static TypeConverter? GetConverterByType(Type type)
  {
    return Converters.Find(x => x.Type == type);
  }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using KDWebServer.ClassHandler.Attributes;
using Newtonsoft.Json;
using NJsonSchema;
using NSwag;

namespace KDWebServer.ClassHandler;

internal class TypeSchemaRegistry
{
  private static readonly JsonSchema AnyValue = new() {
      AllowAdditionalProperties = false,
      Description = "Can be anything: string, number, array, object, etc., including `null`",
  };

  private readonly OpenApiDocument _openApiDocument;
  private readonly Dictionary<string, JsonSchema> _schemaMap = new();

  public TypeSchemaRegistry(OpenApiDocument openApiDocument)
  {
    _openApiDocument = openApiDocument;

    openApiDocument.Definitions.Add("AnyValue", AnyValue);
  }

  private static string GenerateTypeFullName(Type type)
  {
    if (type.IsGenericType) {
      if (type.DeclaringType is null)
        throw new ArgumentException("DeclaringType is null");

      var parentTypeFullName = GenerateTypeFullName(type.DeclaringType);
      var className = type.Name.Replace("`1", "");
      var argsFullNames = type.GenericTypeArguments.Select(GenerateTypeFullName).ToList();
      return $"{parentTypeFullName}_{className}[{string.Join("_", argsFullNames)}]";
    }
    else {
      var typeConverter = SimpleTypeConverters.GetConverterByType(type);
      return typeConverter == null
          ? type.FullName!.Replace(".", "_").Replace("+", "_")
          : typeConverter.RouterTypeName;
    }
  }

  public void ApplyTypeToJsonSchema(Type type, JsonSchema jsonSchema)
  {
    if (type == typeof(object)) {
      jsonSchema.AllowAdditionalProperties = AnyValue.AllowAdditionalProperties;
      jsonSchema.Description = AnyValue.Description;
    }
    else if (SimpleTypeConverters.GetConverterByType(type) is { } typeConverter) {
      typeConverter.ApplyToJsonSchema(jsonSchema);
    }
    else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
      ApplyListTypeToJsonSchema(type, jsonSchema);
    }
    else {
      ApplyCustomTypeToJsonSchema(type, jsonSchema);
    }
  }

  private void ApplyListTypeToJsonSchema(Type type, JsonSchema jsonSchema)
  {
    var listItemType = type.GenericTypeArguments[0];

    var listSchema = new JsonSchema();
    jsonSchema.Type = JsonObjectType.Array;
    ApplyTypeToJsonSchema(listItemType, listSchema);
    jsonSchema.Item = listSchema;
  }

  private void ApplyCustomTypeToJsonSchema(Type type, JsonSchema jsonSchema)
  {
    var typeKey = GenerateTypeFullName(type);

    if (_schemaMap.TryGetValue(typeKey, out var schema)) {
      jsonSchema.Reference = schema;
      return;
    }

    schema = new JsonSchema {
        AllowAdditionalProperties = false,
    };

    var members = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(x => ((MemberInfo)x, x.FieldType))
                      .Concat(type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(x => ((MemberInfo)x, x.PropertyType)));

    foreach (var (memberInfo, fieldType) in members) {
      var jsonSchemaProperty = new JsonSchemaProperty();

      var name = memberInfo.Name;

      var jsonProperty = memberInfo.GetCustomAttribute<JsonPropertyAttribute>();
      if (jsonProperty != null) {
        name = jsonProperty.PropertyName!;
      }

      var exampleAttribute = memberInfo.GetCustomAttribute<ExampleAttribute>();
      if (exampleAttribute != null) {
        jsonSchemaProperty.Example = exampleAttribute.Value;
      }

      if (!Utils.IsNullable(fieldType, out var fieldActualType)) {
        jsonSchemaProperty.IsNullableRaw = true;
        jsonSchemaProperty.IsRequired = false;
      }
      else {
        fieldActualType = fieldType;

        if (jsonProperty == null) {
          jsonSchemaProperty.IsNullableRaw = false;
          jsonSchemaProperty.IsRequired = true;
        }
        else {
          switch (jsonProperty.Required) {
            case Required.Default:
              jsonSchemaProperty.IsNullableRaw = true;
              jsonSchemaProperty.IsRequired = false;
              break;
            case Required.AllowNull:
              jsonSchemaProperty.IsNullableRaw = true;
              jsonSchemaProperty.IsRequired = true;
              break;
            case Required.Always:
              jsonSchemaProperty.IsNullableRaw = false;
              jsonSchemaProperty.IsRequired = true;
              break;
            case Required.DisallowNull:
              jsonSchemaProperty.IsNullableRaw = false;
              jsonSchemaProperty.IsRequired = false;
              break;
            default: throw new ArgumentException("invalid Required value");
          }
        }
      }

      var typeConverter = SimpleTypeConverters.GetConverterByType(fieldActualType);
      if (typeConverter != null) {
        typeConverter.ApplyToJsonSchema(jsonSchemaProperty);
      }
      else {
        ApplyTypeToJsonSchema(fieldActualType, jsonSchemaProperty);
      }

      schema.Properties[name] = jsonSchemaProperty;
    }

    _schemaMap[typeKey] = schema;
    _openApiDocument.Definitions[typeKey] = schema;

    jsonSchema.Reference = schema;
  }
}
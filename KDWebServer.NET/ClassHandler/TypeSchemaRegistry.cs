using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using KDWebServer.ClassHandler.Attributes;
using KDWebServer.ClassHandler.Exceptions;
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
    if (NullabilityUtils.IsNullable(type, out var innerType)) {
      ApplyTypeToJsonSchema(innerType, jsonSchema);
      jsonSchema.IsNullableRaw = true;
      return;
    }

    if (type == typeof(object)) {
      jsonSchema.AllowAdditionalProperties = AnyValue.AllowAdditionalProperties;
      jsonSchema.Description = AnyValue.Description;
    }
    else if (SimpleTypeConverters.GetConverterByType(type) is { } typeConverter) {
      typeConverter.ApplyToJsonSchema(jsonSchema);
    }
    else if (type.IsGenericType) {
      if (type.GetGenericTypeDefinition() == typeof(List<>)) {
        ApplyListTypeToJsonSchema(type, jsonSchema);
      }
      else if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
        ApplyDictionaryTypeToJsonSchema(type, jsonSchema);
      }
      else {
        throw new UnsupportedDataTypeException(type);
      }
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

  private void ApplyDictionaryTypeToJsonSchema(Type type, JsonSchema jsonSchema)
  {
    var dictKeyType = type.GenericTypeArguments[0];
    var dictValueType = type.GenericTypeArguments[1];

    if (dictKeyType != typeof(string)) {
      throw new UnsupportedDataTypeException($"Type /{dictKeyType}/ not supported as an object/dictionary key");
    }

    var itemSchema = new JsonSchema();
    jsonSchema.Type = JsonObjectType.Object;
    ApplyTypeToJsonSchema(dictValueType, itemSchema);
    jsonSchema.AdditionalPropertiesSchema = itemSchema;
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

      var exampleAttribute = memberInfo.GetCustomAttribute<ExampleAttribute>();
      if (exampleAttribute != null) {
        jsonSchemaProperty.Example = exampleAttribute.Value;
      }

      var defaultValueAttribute = memberInfo.GetCustomAttribute<DefaultValueAttribute>();
      if (defaultValueAttribute != null) {
        jsonSchemaProperty.Default = defaultValueAttribute.Value;
      }

      var descriptionAttribute = memberInfo.GetCustomAttribute<DescriptionAttribute>();
      if (descriptionAttribute != null) {
        jsonSchemaProperty.Description = descriptionAttribute.Description;
      }

      DetermineProperties(memberInfo, jsonSchemaProperty, out var name, out var fieldActualType);

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

  private static void DetermineProperties(MemberInfo memberInfo, JsonSchemaProperty jsonSchemaProperty, out string name, out Type fieldActualType)
  {
    var isNullable = NullabilityUtils.IsNullable(memberInfo, out fieldActualType);

    var jsonPropertyAttribute = memberInfo.GetCustomAttribute<JsonPropertyAttribute>();
    var dataMemberAttribute = memberInfo.GetCustomAttribute<DataMemberAttribute>();

    if (jsonPropertyAttribute != null && dataMemberAttribute != null) {
      throw new NotSupportedException("using both JsonProperty and DataMember attributes is not supported");
    }

    if (jsonPropertyAttribute is not null) {
      DeterminePropertiesFromJsonProperty(memberInfo, jsonPropertyAttribute, jsonSchemaProperty, isNullable, out name);
    }
    else if (dataMemberAttribute is not null) {
      DeterminePropertiesFromDataMember(memberInfo, dataMemberAttribute, jsonSchemaProperty, isNullable, out name);
    }
    else {
      name = memberInfo.Name;

      if (isNullable) {
        jsonSchemaProperty.IsNullableRaw = true;
        jsonSchemaProperty.IsRequired = false;
      }
      else {
        jsonSchemaProperty.IsNullableRaw = false;
        jsonSchemaProperty.IsRequired = true;
      }
    }
  }

  private static void DeterminePropertiesFromJsonProperty(MemberInfo memberInfo, JsonPropertyAttribute jsonPropertyAttribute, JsonSchemaProperty jsonSchemaProperty, bool isNullable, out string name)
  {
    name = jsonPropertyAttribute.PropertyName!;

    switch (jsonPropertyAttribute.Required) {
      case Required.Default:
        jsonSchemaProperty.IsNullableRaw = isNullable;
        jsonSchemaProperty.IsRequired = false;
        break;
      case Required.AllowNull:
        if (isNullable)
          throw new ArgumentException($"property {memberInfo.Name} is defined as Required.AllowNull but it is not nullable");

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
      default:
        throw new ArgumentException("invalid Required value");
    }
  }

  private static void DeterminePropertiesFromDataMember(MemberInfo memberInfo, DataMemberAttribute dataMemberAttribute, JsonSchemaProperty jsonSchemaProperty, bool isNullable, out string name)
  {
    name = dataMemberAttribute.Name!;

    jsonSchemaProperty.IsRequired = dataMemberAttribute.IsRequired;
    jsonSchemaProperty.IsNullableRaw = isNullable;
  }
}
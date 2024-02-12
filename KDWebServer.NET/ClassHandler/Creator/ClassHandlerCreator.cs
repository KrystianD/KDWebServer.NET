using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KDWebServer.ClassHandler.Attributes;
using KDWebServer.ClassHandler.Exceptions;
using KDWebServer.ClassHandler.Executor;
using KDWebServer.Handlers.Http;
using NJsonSchema;
using NSwag;

namespace KDWebServer.ClassHandler.Creator;

public static class ClassHandlerCreator
{
  public static void RegisterHandler(WebServer srv, object handler)
  {
    var methods = handler.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);

    var handlerDescriptor = new HandlerDescriptor();

    foreach (var methodInfo in methods) {
      var endpointAttribute = methodInfo.GetCustomAttribute<EndpointAttribute>();
      if (endpointAttribute == null)
        continue;
      var md = ProcessMethod(methodInfo, endpointAttribute, handlerDescriptor);
      handlerDescriptor.Methods.Add(md);
    }

    foreach (var methodDescriptor in handlerDescriptor.Methods) {
      foreach (var endpointAttributeHttpMethod in new[] { methodDescriptor.EndpointAttribute.HttpMethod }) {
        srv.AddEndpoint(methodDescriptor.RouterPath,
                        ctx => ClassHandlerExecutor.HandleRequest(ctx, handler, methodDescriptor),
                        new HashSet<HttpMethod>() {
                            endpointAttributeHttpMethod,
                        },
                        skipDocs: true);
      }
    }

    srv.AppendSwaggerDocument(handlerDescriptor.OpenApiDocument);
  }

  private static MethodDescriptor ProcessMethod(MethodInfo methodInfo, EndpointAttribute endpointAttribute, HandlerDescriptor handlerDescriptor)
  {
    var pathParametersNames = Regex.Matches(endpointAttribute.Endpoint, "{([^}]+)}")
                                   .Select(x => x.Groups[1].Value)
                                   .ToHashSet();

    var hasBody = endpointAttribute.HttpMethod != HttpMethod.Get;

    // get parameters of the actual code method
    var methodParameterDescriptors =
        methodInfo.GetParameters()
                  .Select(parameterInfo => new MethodParameterDescriptor(
                              name: parameterInfo.Name!,
                              parameterInfo: parameterInfo,
                              valueType: parameterInfo.ParameterType,
                              defaultValue: GetParameterDefaultValue(parameterInfo),
                              description: parameterInfo.GetCustomAttribute<DescriptionAttribute>()?.Let(x => x.Description) ?? ""))
                  .ToList();

    // match HTTP path parameters with the method parameters
    foreach (var pathParameterName in pathParametersNames) {
      var methodParameterDescriptor = methodParameterDescriptors.Find(x => x.Name == pathParameterName);
      if (methodParameterDescriptor == null) {
        throw new MethodDescriptorException($"path parameter {pathParameterName} not found in method parameters");
      }

      var typeConverter = SimpleTypeConverters.GetConverterByType(methodParameterDescriptor.ValueType);
      if (typeConverter == null)
        throw new MethodDescriptorException($"path parameter {pathParameterName} type is incorrect for path parameter: {methodParameterDescriptor.ValueType}");

      methodParameterDescriptor.Type = ParameterType.Path;
      methodParameterDescriptor.PathTypeConverter = typeConverter;
    }

    foreach (var methodParameterDescriptor in methodParameterDescriptors.Where(x => x.Type == null)) {
      if (methodParameterDescriptor.ValueType == typeof(HttpRequestContext)) {
        methodParameterDescriptor.Type = ParameterType.Context;
      }
    }

    // assign Query or Body to parameter descriptors
    MethodParameterDescriptor? bodyParameterDescriptor = null;
    JsonSchema? bodyJsonSchema = null;
    foreach (var methodParameterDescriptor in methodParameterDescriptors.Where(x => x.Type == null)) {
      var typeConverter = SimpleTypeConverters.GetConverterByType(methodParameterDescriptor.ValueType);
      if (hasBody) {
        if (typeConverter != null) {
          methodParameterDescriptor.Type = ParameterType.Query;
          methodParameterDescriptor.QueryTypeConverter = typeConverter;
          methodParameterDescriptor.QueryIsNullable = methodParameterDescriptor.DefaultValue.HasDefaultValue || NullabilityUtils.IsNullable(methodParameterDescriptor.ParameterInfo, out _);
        }
        else {
          if (bodyParameterDescriptor == null) {
            methodParameterDescriptor.Type = ParameterType.Body;
            bodyParameterDescriptor = methodParameterDescriptor;

            var type = methodParameterDescriptor.ValueType;
            var jsonSchema = new JsonSchema();
            handlerDescriptor.TypeSchemaRegistry.ApplyTypeToJsonSchema(type, jsonSchema);
            bodyJsonSchema = jsonSchema;
          }
          else {
            throw new MethodDescriptorException($"only one body parameter is allowed. Used as body: {bodyParameterDescriptor.Name}, about to be used: {methodParameterDescriptor.Name}");
          }
        }
      }
      else { // endpoint doesn't contain the body
        if (typeConverter != null) {
          methodParameterDescriptor.Type = ParameterType.Query;
          methodParameterDescriptor.QueryTypeConverter = typeConverter;
          methodParameterDescriptor.QueryIsNullable = methodParameterDescriptor.DefaultValue.HasDefaultValue || NullabilityUtils.IsNullable(methodParameterDescriptor.ParameterInfo, out _);
        }
        else {
          throw new MethodDescriptorException($"query parameter {methodParameterDescriptor.Name} type is incorrect for query parameter: {methodParameterDescriptor.ValueType}");
        }
      }
    }

    OpenApiPathItem? item;
    if (!handlerDescriptor.OpenApiDocument.Paths.TryGetValue(endpointAttribute.Endpoint, out item)) {
      item = new OpenApiPathItem();
      handlerDescriptor.OpenApiDocument.Paths[endpointAttribute.Endpoint] = item;
    }

    var op = new OpenApiOperation();

    foreach (var descriptor in methodParameterDescriptors.Where(x => x.Type == ParameterType.Path)) {
      var p = new OpenApiParameter();
      p.Name = descriptor.Name;
      p.Kind = OpenApiParameterKind.Path;
      p.IsRequired = true;

      p.Schema = new JsonSchema();
      handlerDescriptor.TypeSchemaRegistry.ApplyTypeToJsonSchema(descriptor.ValueType, p.Schema);
      FillFromDesc(p, descriptor);
      op.Parameters.Add(p);
    }

    foreach (var descriptor in methodParameterDescriptors.Where(x => x.Type == ParameterType.Query)) {
      var p = new OpenApiParameter();
      p.Name = descriptor.Name;
      p.Kind = OpenApiParameterKind.Query;
      p.IsRequired = !descriptor.QueryIsNullable!.Value;

      p.Schema = new JsonSchema();
      handlerDescriptor.TypeSchemaRegistry.ApplyTypeToJsonSchema(descriptor.ValueType, p.Schema);
      FillFromDesc(p, descriptor);
      op.Parameters.Add(p);
    }

    if (bodyParameterDescriptor != null) {
      op.RequestBody = new OpenApiRequestBody();
      op.RequestBody.Content.Add("application/json", new OpenApiMediaType() {
              Schema = bodyJsonSchema,
          }
      );
    }

    var response = new OpenApiResponse() {
        Description = methodInfo.GetCustomAttribute<ReturnDescriptionAttribute>()?.Let(x => x.Description) ?? "",
    };

    var ret = methodInfo.ReturnType;
    var retType = ret.BaseType == typeof(Task) ? ret.GenericTypeArguments[0] : ret;

    var methodResponseType = methodInfo.GetCustomAttribute<ResponseTypeAttribute>()?.Let(x => x.Type) ?? ResponseTypeEnum.Json;

    if (methodResponseType == ResponseTypeEnum.Text) {
      if (retType == typeof(string)) {
        var od = new OpenApiMediaType() {
            Schema = new JsonSchema() {
                Type = JsonObjectType.String,
            },
        };

        response.Content.Add("application/text", od);
      }
      else {
        throw new MethodDescriptorException("method defined as ResponseTypeEnum.Text must return string or Task<string>");
      }
    }
    else {
      if (retType != typeof(void)) {
        var jsonSchema = new JsonSchema();
        handlerDescriptor.TypeSchemaRegistry.ApplyTypeToJsonSchema(retType, jsonSchema);
        var od = new OpenApiMediaType() {
            Schema = jsonSchema,
        };

        response.Content.Add("application/json", od);
      }
    }

    op.Responses.Add("200", response);

    var categoryAttribute = methodInfo.GetCustomAttribute<CategoryAttribute>();
    if (categoryAttribute != null) {
      op.Tags = new List<string>() {
          categoryAttribute.Category,
      };
    }

    var descriptionAttribute = methodInfo.GetCustomAttribute<DescriptionAttribute>();
    if (descriptionAttribute != null) {
      op.Summary = descriptionAttribute.Description;
      op.Description = descriptionAttribute.Description;
    }

    item.Add(endpointAttribute.HttpMethod.ToString(), op);

    var routerPath = methodParameterDescriptors
                     .Where(x => x.Type == ParameterType.Path)
                     .Aggregate(endpointAttribute.Endpoint, (current, x) => current.Replace($"{{{x.Name}}}", $"<string:{x.Name}>"));

    return new MethodDescriptor(
        MethodInfo: methodInfo,
        EndpointAttribute: endpointAttribute,
        MethodParameterDescriptors: methodParameterDescriptors,
        RouterPath: routerPath,
        BodyJsonSchema: bodyJsonSchema,
        MethodResponseType: methodResponseType);
  }

  private static DefaultValue GetParameterDefaultValue(ParameterInfo parameterInfo)
  {
    var defaultValueAttribute = parameterInfo.GetCustomAttribute<DefaultValueAttribute>();

    if (defaultValueAttribute != null) {
      return new DefaultValue(true, defaultValueAttribute.Value);
    }
    else if (parameterInfo.DefaultValue is not DBNull) {
      return new DefaultValue(true, parameterInfo.DefaultValue);
    }
    else {
      return new DefaultValue(false, null);
    }
  }

  private static void FillFromDesc(OpenApiParameter p, MethodParameterDescriptor pd)
  {
    if (pd.DefaultValue.HasDefaultValue)
      p.Default = pd.DefaultValue.Value;

    p.Description = pd.Description;
  }
}
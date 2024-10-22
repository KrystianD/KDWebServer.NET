using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using KDWebServer.ClassHandler.Attributes;
using KDWebServer.ClassHandler.Exceptions;
using KDWebServer.ClassHandler.Executor;
using KDWebServer.Handlers.Http;
using KDWebServer.Handlers.Websocket;
using NJsonSchema;
using NSwag;

namespace KDWebServer.ClassHandler.Creator;

[PublicAPI]
public static class ClassHandlerCreator
{
  private record EndpointDefinitionWithHandler(EndpointDefinition EndpointDefinition, Func<object?[], object?> Handler);

  public static void RegisterHandler(WebServer srv, object handler, string prefix = "")
  {
    prefix = prefix.TrimEnd('/');

    var endpoints = new List<EndpointDefinitionWithHandler>();

    var methods = handler.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);

    foreach (var methodInfo in methods) {
      var endpointAttribute = methodInfo.GetCustomAttribute<EndpointAttribute>();
      if (endpointAttribute == null)
        continue;

      var ret = methodInfo.ReturnType;
      var retType = ret == typeof(Task)
          ? typeof(void)
          : ret.BaseType == typeof(Task)
              ? ret.GenericTypeArguments[0]
              : ret;

      var endpointBuilder = EndpointDefinition.Create(prefix + endpointAttribute.Endpoint, endpointAttribute.HttpMethod)
                                              .WithReturnType(retType);

      foreach (var parameterInfo in methodInfo.GetParameters()) {
        endpointBuilder.AddParameter(name: parameterInfo.Name!,
                                     type: parameterInfo.ParameterType,
                                     isNullable: NullabilityUtils.IsNullable(parameterInfo, out _),
                                     parameterBuilder: builder => {
                                       builder.WithDescription(parameterInfo.GetCustomAttribute<DescriptionAttribute>()?.Let(x => x.Description) ?? "");

                                       var defaultValue = GetParameterDefaultValue(parameterInfo);
                                       if (defaultValue.HasDefaultValue)
                                         builder.WithDefaultValue(defaultValue.Value!);
                                     });
      }

      var categoryAttribute = methodInfo.GetCustomAttribute<CategoryAttribute>();
      if (categoryAttribute != null) {
        endpointBuilder.WithCategory(categoryAttribute.Category);
      }

      var descriptionAttribute = methodInfo.GetCustomAttribute<DescriptionAttribute>();
      if (descriptionAttribute != null) {
        endpointBuilder.WithDescription(descriptionAttribute.Description);
      }

      endpointBuilder.WithReturnDescription(methodInfo.GetCustomAttribute<ReturnDescriptionAttribute>()?.Let(x => x.Description) ?? "");

      var obsoleteAttribute = methodInfo.GetCustomAttribute<ObsoleteAttribute>();
      if (obsoleteAttribute != null) {
        endpointBuilder.WithDeprecated(true);
      }

      if (methodInfo.GetCustomAttribute<RunOnThreadPoolAttribute>() != null) {
        endpointBuilder.WithRunOnThreadPool(true);
      }

      var responseType = methodInfo.GetCustomAttribute<ResponseTypeAttribute>()?.Let(x => x.Type);
      if (responseType != null)
        endpointBuilder.WithResponseType(responseType.Value);

      endpoints.Add(new EndpointDefinitionWithHandler(endpointBuilder.Build(),
                                                      parameters => methodInfo.Invoke(handler, parameters)));
    }

    foreach (var endpointDef in endpoints) {
      RegisterEndpoint(srv, endpointDef.EndpointDefinition, endpointDef.Handler);
    }
  }

  public static void RegisterEndpoint(WebServer srv, EndpointDefinition endpointDefinition, Func<object?[], Task<object?>> handler)
  {
    RegisterEndpoint(srv, endpointDefinition, (Func<object?[], object?>)handler);
  }

  public static void RegisterEndpoint(WebServer srv, EndpointDefinition endpointDefinition, Func<object?[], object?> handler)
  {
    var endpointDescriptor = CreateEndpointDescriptor(endpointDefinition);

    srv.AddEndpoint(endpointDescriptor.RouterPath,
                    async ctx => {
                      var args = ClassHandlerExecutor.ParseArgs(ctx, endpointDescriptor);
                      return await ClassHandlerExecutor.ExecuteHandler(endpointDescriptor, args, handler);
                    },
                    new HashSet<HttpMethod>() { endpointDefinition.HttpMethod },
                    skipDocs: true,
                    runOnThreadPool: endpointDefinition.RunOnThreadPool);

    srv.AppendSwaggerDocument(endpointDescriptor.OpenApiDocument);
  }

  public static void RegisterWebsocketEndpoint(WebServer srv, EndpointDefinition endpointDefinition, Func<WebsocketRequestContext, object?[], CancellationToken, Task> handler)
  {
    var endpointDescriptor = CreateEndpointDescriptor(endpointDefinition);

    srv.AddWsEndpoint(endpointDescriptor.RouterPath,
                      async (ctx, token) => {
                        var args = ClassHandlerExecutor.ParseArgs(ctx, endpointDescriptor);
                        await handler(ctx, args, token);
                      },
                      skipDocs: true,
                      runOnThreadPool: endpointDefinition.RunOnThreadPool);

    srv.AppendSwaggerDocument(endpointDescriptor.OpenApiDocument);
  }

  private static EndpointDescriptor CreateEndpointDescriptor(EndpointDefinition endpointDefinition)
  {
    var openApiDocument = new OpenApiDocument();
    var typeSchemaRegistry = new TypeSchemaRegistry(openApiDocument);

    var endpointPath = endpointDefinition.Path;

    var pathParametersNames = Regex.Matches(endpointPath, "{([^}]+)}")
                                   .Select(x => x.Groups[1].Value)
                                   .ToHashSet();

    var hasBody = endpointDefinition.HttpMethod != HttpMethod.Get;

    // get parameters of the actual code method
    var methodParameterDescriptors = endpointDefinition.Parameters
                                                       .Select(parameterInfo => new MethodParameterDescriptor(
                                                                   name: parameterInfo.Name,
                                                                   valueType: parameterInfo.Type,
                                                                   isNullable: parameterInfo.IsNullable,
                                                                   parameterBuilder: parameterInfo.ParameterBuilder))
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

      methodParameterDescriptor.Kind = ParameterKind.Path;
      methodParameterDescriptor.PathTypeConverter = typeConverter;
    }

    foreach (var methodParameterDescriptor in methodParameterDescriptors.Where(x => x.Kind == null)) {
      if (methodParameterDescriptor.ValueType == typeof(HttpRequestContext)) {
        methodParameterDescriptor.Kind = ParameterKind.Context;
      }
    }

    // assign Query or Body to parameter descriptors
    MethodParameterDescriptor? bodyParameterDescriptor = null;
    JsonSchema? bodyJsonSchema = null;
    foreach (var methodParameterDescriptor in methodParameterDescriptors.Where(x => x.Kind == null)) {
      var simpleTypeConverter = SimpleTypeConverters.GetConverterByType(methodParameterDescriptor.ValueType);
      if (hasBody) {
        var bodyTypeConverter = BodyTypeConverters.GetConverterByType(methodParameterDescriptor.ValueType);
        if (bodyTypeConverter == null && simpleTypeConverter != null) {
          methodParameterDescriptor.Kind = ParameterKind.Query;
          methodParameterDescriptor.QueryTypeConverter = simpleTypeConverter;
          methodParameterDescriptor.QueryIsNullable = methodParameterDescriptor.ParameterBuilder.DefaultValue.HasDefaultValue || methodParameterDescriptor.IsNullable;
        }
        else {
          if (bodyParameterDescriptor == null) {
            methodParameterDescriptor.Kind = ParameterKind.Body;
            bodyParameterDescriptor = methodParameterDescriptor;

            bodyJsonSchema = new JsonSchema();

            if (bodyTypeConverter == null) {
              var type = methodParameterDescriptor.ValueType;
              typeSchemaRegistry.ApplyTypeToJsonSchema(type, bodyJsonSchema);
            }
            else {
              bodyTypeConverter.ApplyToJsonSchema(bodyJsonSchema);
            }
          }
          else {
            throw new MethodDescriptorException($"only one body parameter is allowed. Used as body: {bodyParameterDescriptor.Name}, about to be used: {methodParameterDescriptor.Name}");
          }
        }
      }
      else { // endpoint doesn't contain the body
        if (simpleTypeConverter != null) {
          methodParameterDescriptor.Kind = ParameterKind.Query;
          methodParameterDescriptor.QueryTypeConverter = simpleTypeConverter;
          methodParameterDescriptor.QueryIsNullable = methodParameterDescriptor.ParameterBuilder.DefaultValue.HasDefaultValue || methodParameterDescriptor.IsNullable;
        }
        else {
          throw new MethodDescriptorException($"query parameter {methodParameterDescriptor.Name} type is incorrect for query parameter: {methodParameterDescriptor.ValueType}");
        }
      }
    }

    OpenApiPathItem? item;
    if (!openApiDocument.Paths.TryGetValue(endpointPath, out item)) {
      item = new OpenApiPathItem();
      openApiDocument.Paths[endpointPath] = item;
    }

    var op = new OpenApiOperation();

    foreach (var descriptor in methodParameterDescriptors.Where(x => x.Kind == ParameterKind.Path)) {
      var p = new OpenApiParameter();
      p.Name = descriptor.Name;
      p.Kind = OpenApiParameterKind.Path;
      p.IsRequired = true;

      p.Schema = new JsonSchema();
      typeSchemaRegistry.ApplyTypeToJsonSchema(descriptor.ValueType, p.Schema);
      FillFromDesc(p, descriptor);
      foreach (var value in descriptor.ParameterBuilder.DropdownItems)
        p.Schema.Enumeration.Add(value);
      op.Parameters.Add(p);
    }

    foreach (var descriptor in methodParameterDescriptors.Where(x => x.Kind == ParameterKind.Query)) {
      var p = new OpenApiParameter();
      p.Name = descriptor.Name;
      p.Kind = OpenApiParameterKind.Query;
      p.IsRequired = !descriptor.QueryIsNullable!.Value;

      p.Schema = new JsonSchema();
      typeSchemaRegistry.ApplyTypeToJsonSchema(descriptor.ValueType, p.Schema);
      FillFromDesc(p, descriptor);
      foreach (var value in descriptor.ParameterBuilder.DropdownItems)
        p.Schema.Enumeration.Add(value);
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
        Description = endpointDefinition.ReturnDescription,
    };

    var retType = endpointDefinition.ReturnType;

    var methodResponseType = endpointDefinition.ResponseType;

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
        typeSchemaRegistry.ApplyTypeToJsonSchema(retType, jsonSchema);
        var od = new OpenApiMediaType() {
            Schema = jsonSchema,
        };

        response.Content.Add("application/json", od);
      }
    }

    op.Responses.Add("200", response);

    var category = endpointDefinition.Category;
    if (category != "") {
      op.Tags = new List<string>() { category };
    }

    var description = endpointDefinition.Description;
    if (description != "") {
      op.Summary = description;
      op.Description = description;
    }

    if (endpointDefinition.IsDeprecated) {
      op.IsDeprecated = true;
    }

    item.Add(endpointDefinition.HttpMethod.ToString(), op);

    var routerPath = methodParameterDescriptors
                     .Where(x => x.Kind == ParameterKind.Path)
                     .Aggregate(endpointDefinition.Path, (current, x) => current.Replace($"{{{x.Name}}}", $"<string:{x.Name}>"));

    return new EndpointDescriptor(
        Definition: endpointDefinition,
        OpenApiDocument: openApiDocument,
        TypeSchemaRegistry: typeSchemaRegistry,
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
    if (pd.ParameterBuilder.DefaultValue.HasDefaultValue)
      p.Default = pd.ParameterBuilder.DefaultValue.Value;

    p.Description = pd.ParameterBuilder.Description;
  }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KDWebServer.ClassHandler.Attributes;
using KDWebServer.ClassHandler.Creator;
using KDWebServer.Handlers;
using KDWebServer.Handlers.Http;

namespace KDWebServer.ClassHandler.Executor;

internal static class ClassHandlerExecutor
{
  public static object?[] ParseArgs(IRequestContext ctx, EndpointDescriptor endpointDescriptor)
  {
    var pathParams = ctx.Params;

    var call = new List<object?>();
    foreach (var methodParameterDescriptor in endpointDescriptor.MethodParameterDescriptors) {
      var name = methodParameterDescriptor.Name;
      var type = methodParameterDescriptor.ValueType;

      switch (methodParameterDescriptor.Kind) {
        case ParameterKind.Path:
          var pathValue = (string)pathParams[methodParameterDescriptor.Name];
          try {
            call.Add(methodParameterDescriptor.PathTypeConverter!.FromStringConverter(pathValue));
          }
          catch {
            var s = $"error during parsing path parameter: /{name}/, expected type: /{type}/, got value of: /{pathValue}/";
            throw Response.StatusCode(400, s);
          }

          break;
        case ParameterKind.Query:
          if (ctx.QueryString.TryGetString(methodParameterDescriptor.Name, out var queryValue)) {
            try {
              call.Add(methodParameterDescriptor.QueryTypeConverter!.FromStringConverter(queryValue));
            }
            catch {
              var s = $"error during parsing query parameter: /{name}/, expected type: /{type}/, got value of: /{queryValue}/";
              throw Response.StatusCode(400, s);
            }
          }
          else if (methodParameterDescriptor.ParameterBuilder.DefaultValue.HasDefaultValue) {
            call.Add(methodParameterDescriptor.ParameterBuilder.DefaultValue.Value);
          }
          else if (methodParameterDescriptor.QueryIsNullable!.Value) {
            call.Add(null);
          }
          else {
            var s = $"no param {methodParameterDescriptor.Name}";
            throw Response.StatusCode(400, s);
          }

          break;
        case ParameterKind.Body:
          if (ctx is HttpRequestContext httpRequestContext) {
            var jsonData = httpRequestContext.JsonData;
            if (jsonData is null) {
              throw Response.StatusCode(400, "body is required");
            }

            var errors = endpointDescriptor.BodyJsonSchema!.Validate(jsonData);

            if (errors.Count > 0) {
              var s = "validation errors:\n" + string.Join("\n", errors.Select(x => $"- {x}"));
              throw Response.StatusCode(400, s);
            }

            call.Add(jsonData.ToObject(methodParameterDescriptor.ValueType, Consts.DefaultSerializer)!);
          }
          else {
            throw new ArgumentException("invalid parameter type");
          }

          break;
        case ParameterKind.Context:
          call.Add(ctx);
          break;
        default:
          throw new ArgumentException("invalid parameter type");
      }
    }

    return call.ToArray();
  }

  private static async Task<object?> InvokeHandler(object?[] args, Func<object?[], object?> handler)
  {
    try {
      var resp = handler(args);
      if (resp is Task task) {
        await task;

        var taskType = task.GetType();
        if (taskType.IsGenericType &&
            (taskType.GetGenericTypeDefinition() == typeof(Task<>) || // for async Task<> without an async operation
             taskType.GetGenericTypeDefinition().DeclaringType == typeof(System.Runtime.CompilerServices.AsyncTaskMethodBuilder<>)) && // for async Task<> with an async operation
            taskType.GetGenericArguments()[0] != Type.GetType("System.Threading.Tasks.VoidTaskResult")) {
          return ((dynamic)task).Result;
        }
        else {
          return null;
        }
      }
      else {
        return resp;
      }
    }
    catch (TargetInvocationException e) {
      if (e.InnerException == null) {
        throw new Exception("unknown exception");
      }
      else {
        throw e.InnerException;
      }
    }
  }

  public static async Task<WebServerResponse> ExecuteHandler(EndpointDescriptor endpointDescriptor, object?[] args, Func<object?[], object?> handler)
  {
    try {
      var res = await InvokeHandler(args, handler);

      return res switch {
          WebServerResponse webServerResponse => webServerResponse,
          null => Response.StatusCode(200),
          _ => endpointDescriptor.MethodResponseType switch {
              ResponseTypeEnum.Json => Response.Json(res),
              ResponseTypeEnum.Text => Response.Text((string)res),
              _ => throw new Exception("invalid enum value"),
          },
      };
    }
    catch (Exception e) {
      if (endpointDescriptor.Definition.ErrorHandlerMiddlewareFactory != null) {
        var errorHandlerMiddleware = endpointDescriptor.Definition.ErrorHandlerMiddlewareFactory();
        return errorHandlerMiddleware.Process(e);
      }
      else {
        throw;
      }
    }
  }
}
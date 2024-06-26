using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KDWebServer.ClassHandler.Attributes;
using KDWebServer.ClassHandler.Creator;
using KDWebServer.Handlers.Http;

namespace KDWebServer.ClassHandler.Executor;

internal static class ClassHandlerExecutor
{
  public static async Task<WebServerResponse> HandleRequest(HttpRequestContext ctx, object handler, MethodDescriptor methodDescriptor)
  {
    var pathParams = ctx.Params;

    var call = new List<object?>();
    foreach (var methodParameterDescriptor in methodDescriptor.MethodParameterDescriptors) {
      var name = methodParameterDescriptor.Name;
      var type = methodParameterDescriptor.ValueType;

      switch (methodParameterDescriptor.Type) {
        case ParameterType.Path:
          var pathValue = (string)pathParams[methodParameterDescriptor.Name];
          try {
            call.Add(methodParameterDescriptor.PathTypeConverter!.FromStringConverter(pathValue));
          }
          catch {
            var s = $"error during parsing path parameter: /{name}/, expected type: /{type}/, got value of: /{pathValue}/";
            return Response.StatusCode(400, s);
          }

          break;
        case ParameterType.Query:
          if (ctx.QueryString.TryGetString(methodParameterDescriptor.Name, out var queryValue)) {
            try {
              call.Add(methodParameterDescriptor.QueryTypeConverter!.FromStringConverter(queryValue));
            }
            catch {
              var s = $"error during parsing query parameter: /{name}/, expected type: /{type}/, got value of: /{queryValue}/";
              return Response.StatusCode(400, s);
            }
          }
          else if (methodParameterDescriptor.DefaultValue.HasDefaultValue) {
            call.Add(methodParameterDescriptor.DefaultValue.Value);
          }
          else if (methodParameterDescriptor.QueryIsNullable!.Value) {
            call.Add(null);
          }
          else {
            var s = $"no param {methodParameterDescriptor.Name}";
            return Response.StatusCode(400, s);
          }

          break;
        case ParameterType.Body:
          var jsonData = ctx.JsonData;
          if (jsonData is null) {
            return Response.StatusCode(400, "body is required");
          }

          var errors = methodDescriptor.BodyJsonSchema!.Validate(jsonData);

          if (errors.Count > 0) {
            var s = "validation errors:\n" + string.Join("\n", errors.Select(x => $"- {x}"));
            return Response.StatusCode(400, s);
          }

          call.Add(jsonData.ToObject(methodParameterDescriptor.ValueType)!);
          break;
        case ParameterType.Context:
          call.Add(ctx);
          break;
        default:
          throw new ArgumentException("invalid parameter type");
      }
    }

    try {
      var res = methodDescriptor.MethodInfo.Invoke(handler, call.ToArray());
      if (res is Task task) {
        await task;

        var taskType = task.GetType();
        if (taskType.IsGenericType &&
            (taskType.GetGenericTypeDefinition() == typeof(Task<>) || // for async Task<> without an async operation
             taskType.GetGenericTypeDefinition().DeclaringType == typeof(System.Runtime.CompilerServices.AsyncTaskMethodBuilder<>)) && // for async Task<> with an async operation
            taskType.GetGenericArguments()[0] != Type.GetType("System.Threading.Tasks.VoidTaskResult")) {
          res = ((dynamic)task).Result;
        }
        else {
          res = null;
        }
      }

      return res switch {
          WebServerResponse resp => resp,
          null => Response.StatusCode(200),
          _ => methodDescriptor.MethodResponseType switch {
              ResponseTypeEnum.Json => Response.Json(res),
              ResponseTypeEnum.Text => Response.Text((string)res),
              _ => throw new Exception("invalid enum value"),
          },
      };
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
}
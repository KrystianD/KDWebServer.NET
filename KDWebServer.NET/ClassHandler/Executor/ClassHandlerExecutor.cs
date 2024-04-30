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
  public static async Task<WebServerResponse> HandleRequest(HttpRequestContext ctx, EndpointDescriptor endpointDescriptor, Func<object?[], object?> handler)
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
            return Response.StatusCode(400, s);
          }

          break;
        case ParameterKind.Query:
          if (ctx.QueryString.TryGetString(methodParameterDescriptor.Name, out var queryValue)) {
            try {
              call.Add(methodParameterDescriptor.QueryTypeConverter!.FromStringConverter(queryValue));
            }
            catch {
              var s = $"error during parsing query parameter: /{name}/, expected type: /{type}/, got value of: /{queryValue}/";
              return Response.StatusCode(400, s);
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
            return Response.StatusCode(400, s);
          }

          break;
        case ParameterKind.Body:
          var jsonData = ctx.JsonData;
          if (jsonData is null) {
            return Response.StatusCode(400, "body is required");
          }

          var errors = endpointDescriptor.BodyJsonSchema!.Validate(jsonData);

          if (errors.Count > 0) {
            var s = "validation errors:\n" + string.Join("\n", errors.Select(x => $"- {x}"));
            return Response.StatusCode(400, s);
          }

          call.Add(jsonData.ToObject(methodParameterDescriptor.ValueType)!);
          break;
        case ParameterKind.Context:
          call.Add(ctx);
          break;
        default:
          throw new ArgumentException("invalid parameter type");
      }
    }

    try {
      var res = handler(call.ToArray());
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
          _ => endpointDescriptor.MethodResponseType switch {
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
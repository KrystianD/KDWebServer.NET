using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
          else {
            var s = $"no param {methodParameterDescriptor.Name}";
            return Response.StatusCode(400, s);
          }

          break;
        case ParameterType.Body:
          var jsonData = ctx.JsonData!;

          var errors = methodDescriptor.BodyJsonSchema!.Validate(jsonData);

          if (errors.Count > 0) {
            var s = "validation errors:\n" + string.Join("\n", errors.Select(x => $"- {x}"));
            return Response.StatusCode(400, s);
          }

          call.Add(jsonData.ToObject(methodParameterDescriptor.ValueType)!);
          break;
        default:
          throw new ArgumentException("invalid parameter type");
      }
    }

    try {
      var res = methodDescriptor.MethodInfo.Invoke(handler, call.ToArray());
      if (res is Task task) {
        await task;
        res = ((dynamic)task).Result;
      }

      return res switch {
          WebServerResponse resp => resp,
          null => Response.StatusCode(200),
          _ => Response.Json(res),
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
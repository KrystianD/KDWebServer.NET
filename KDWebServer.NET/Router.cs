using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using NJsonSchema;
using NSwag;

namespace KDWebServer
{
  internal static class Router
  {
    public class RouteDescriptor
    {
      public class ParameterDescriptor
      {
        public readonly OpenApiParameter OpenApiParameter;
        public readonly Func<string, object> Converter;

        public ParameterDescriptor(OpenApiParameter openApiParameter, Func<string, object> converter)
        {
          OpenApiParameter = openApiParameter;
          Converter = converter;
        }
      }

      public Regex Regex;
      public readonly Dictionary<string, ParameterDescriptor> Params = new();
      public int Score;
      public HashSet<HttpMethod> Methods;
      public string OpanApiPath;

      public bool TryMatch(string path, out RouteMatch match)
      {
        match = new RouteMatch();

        Match m = Regex.Match(path);
        if (!m.Success)
          return false;

        foreach (var (name, parameterDescriptor) in Params)
          match.Params.Add(name, parameterDescriptor.Converter(m.Groups[name].Value));

        return true;
      }
    }

    public class RouteMatch
    {
      public readonly Dictionary<string, object> Params = new();
    }

    public static RouteDescriptor CompileRoute(string route)
    {
      const string ParamRegex = "<(?<type>[a-z]+):(?<name>[a-zA-Z0-9_-]+)>";

      int score = 100;

      string routeRegex;
      if (route == "*") {
        routeRegex = "$";
        score = 50;
      }
      else {
        routeRegex = $"^{Regex.Escape(route)}$";
      }

      var routeDesc = new RouteDescriptor();

      routeDesc.OpanApiPath = Regex.Replace(route, ParamRegex, match => $"{{{match.Groups["name"].Value}}}");

      bool hasRegex = false;
      string r = Regex.Replace(routeRegex, ParamRegex, match => {
        string type = match.Groups["type"].Value;
        string name = match.Groups["name"].Value;

        hasRegex = true;

        routeDesc.Params.Add(name, type switch {
            "string" => new RouteDescriptor.ParameterDescriptor(
                new() { Name = name, Kind = OpenApiParameterKind.Path, Type = JsonObjectType.String },
                s => s),
            "int" => new RouteDescriptor.ParameterDescriptor(
                new() { Name = name, Kind = OpenApiParameterKind.Path, Type = JsonObjectType.Number, Format = "int32" },
                s => int.TryParse(s, out var v) ? v : throw new RouteInvalidValueProvidedException()),
            "long" => new RouteDescriptor.ParameterDescriptor(
                new() { Name = name, Kind = OpenApiParameterKind.Path, Type = JsonObjectType.Number, Format = "int64" },
                s => long.TryParse(s, out var v) ? v : throw new RouteInvalidValueProvidedException()),
            _ => throw new Exception("invalid route parameter type"),
        });

        return "(?<" + name + ">[^/]+)";
      });

      if (hasRegex)
        score -= 10;

      routeDesc.Regex = new Regex(r);
      routeDesc.Score = score;

      return routeDesc;
    }
  }
}
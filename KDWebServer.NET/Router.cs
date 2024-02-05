using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using KDWebServer.Exceptions;
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
        Match m = Regex.Match(path);
        match = new RouteMatch(this, m);
        return m.Success;
      }
    }

    internal class RouteMatch
    {
      private readonly RouteDescriptor Descriptor;
      private readonly Match RegexMatch;

      public RouteMatch(RouteDescriptor descriptor, Match regexMatch)
      {
        Descriptor = descriptor;
        RegexMatch = regexMatch;
      }

      public void ParseParams(out Dictionary<string, object> routeParams)
      {
        routeParams = new Dictionary<string, object>();
        foreach (var (name, parameterDescriptor) in Descriptor.Params) {
          routeParams.Add(name, parameterDescriptor.Converter(RegexMatch.Groups[name].Value));
        }
      }
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
                new() {
                    Name = name, Kind = OpenApiParameterKind.Path, IsRequired = true,
                    Type = JsonObjectType.String,
                },
                s => s),
            "int" => new RouteDescriptor.ParameterDescriptor(
                new() {
                    Name = name, Kind = OpenApiParameterKind.Path, IsRequired = true,
                    Type = JsonObjectType.Number, Format = "int32",
                },
                s => int.TryParse(s, out var v) ? v : throw new RouteInvalidValueProvidedException(name, type, s)),
            "long" => new RouteDescriptor.ParameterDescriptor(
                new() {
                    Name = name, Kind = OpenApiParameterKind.Path, IsRequired = true,
                    Type = JsonObjectType.Number, Format = "int64",
                },
                s => long.TryParse(s, out var v) ? v : throw new RouteInvalidValueProvidedException(name, type, s)),
            "guid" => new RouteDescriptor.ParameterDescriptor(
                new() {
                    Name = name, Kind = OpenApiParameterKind.Path, IsRequired = true,
                    Type = JsonObjectType.String, Format = "uuid",
                },
                s => Guid.TryParse(s, out var v) ? v : throw new RouteInvalidValueProvidedException(name, type, s)),
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
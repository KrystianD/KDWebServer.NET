using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KDWebServer.Exceptions;

namespace KDWebServer;

internal static class Router
{
  public class RouteDescriptor
  {
    private readonly Regex Regex;
    public readonly Dictionary<string, SimpleTypeConverters.TypeConverter> Params;
    public readonly int Score;
    public readonly string OpanApiPath;

    public RouteDescriptor(Regex regex, Dictionary<string, SimpleTypeConverters.TypeConverter> @params, int score, string opanApiPath)
    {
      Regex = regex;
      Params = @params;
      Score = score;
      OpanApiPath = opanApiPath;
    }

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
        var valueStr = RegexMatch.Groups[name].Value;
        try {
          var value = parameterDescriptor.FromStringConverter(valueStr);
          routeParams.Add(name, value);
        }
        catch {
          throw new RouteInvalidValueProvidedException(name, parameterDescriptor.RouterTypeName, valueStr);
        }
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

    var paramsDict = new Dictionary<string, SimpleTypeConverters.TypeConverter>();
    bool hasRegex = false;
    string r = Regex.Replace(routeRegex, ParamRegex, match => {
      string type = match.Groups["type"].Value;
      string name = match.Groups["name"].Value;

      hasRegex = true;

      var knownTypeConverter = SimpleTypeConverters.GetConverterByRouterTypeName(type);
      if (knownTypeConverter == null)
        throw new Exception("invalid route parameter type");

      paramsDict.Add(name, knownTypeConverter);

      return "(?<" + name + ">[^/]+)";
    });

    if (hasRegex)
      score -= 10;

    return new RouteDescriptor(
        regex: new Regex(r),
        @params: paramsDict,
        score: score,
        opanApiPath: Regex.Replace(route, ParamRegex, match => $"{{{match.Groups["name"].Value}}}"));
  }
}
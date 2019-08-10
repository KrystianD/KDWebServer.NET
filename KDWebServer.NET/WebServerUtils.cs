using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace KDWebServer
{
  public static class WebServerUtils
  {
    public class RouteInvalidValueProvidedException : Exception { }

    public class RouteDescriptor
    {
      public Regex Regex;
      public readonly Dictionary<string, Func<string, object>> Params = new Dictionary<string, Func<string, object>>();
      public int Score;
      public HashSet<HttpMethod> Methods;

      public bool TryMatch(string path, out RouteMatch match)
      {
        match = new RouteMatch();

        Match m = Regex.Match(path);
        if (!m.Success)
          return false;

        foreach (var pair in Params) {
          var name = pair.Key;
          var conv = pair.Value;
          match.Params.Add(name, conv(m.Groups[name].Value));
        }
        return true;
      }
    }

    public class RouteMatch
    {
      public readonly Dictionary<string, object> Params = new Dictionary<string, object>();
    }

    public static HttpMethod StringToHttpMethod(string method)
    {
      switch (method.ToUpper()) {
        case "GET": return HttpMethod.Get;
        case "PUT": return HttpMethod.Put;
        case "POST": return HttpMethod.Post;
        case "DELETE": return HttpMethod.Delete;
        case "HEAD": return HttpMethod.Head;
        case "OPTIONS": return HttpMethod.Options;
        case "TRACE": return HttpMethod.Trace;
        default:
          throw new Exception($"invalid method: {method}");
      }
    }

    public static RouteDescriptor CompileRoute(string route)
    {
      int score = 100;

      if (route == "*") {
        route = "$";
        score = 50;
      }
      else {
        route = $"^{Regex.Escape(route)}$";
      }

      var routeDesc = new RouteDescriptor();

      bool hasRegex = false;
      string r = Regex.Replace(route, "<(?<type>[a-z]+):(?<name>[a-z0-9]+)>", match =>
      {
        string type = match.Groups["type"].Value;
        string name = match.Groups["name"].Value;

        hasRegex = true;

        switch (type) {
          case "string":
            routeDesc.Params.Add(name, s => s);
            break;
          case "int":
            routeDesc.Params.Add(name, s =>
            {
              if (!int.TryParse(s, out var v))
                throw new RouteInvalidValueProvidedException();

              return v;
            });
            break;
          default:
            throw new Exception("invalid route type");
        }

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
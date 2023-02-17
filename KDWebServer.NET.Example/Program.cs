using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
#if !NET6_0_OR_GREATER
using MoreLinq.Extensions;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.Targets;

namespace KDWebServer.Example
{
  class Program
  {
    public static string FormatForLog(object val, bool multiline, bool keepShort)
    {
      return val switch {
          null => "<null>",
          string v => v,
          IPEndPoint v => v.ToString(),
          IPAddress v => v.ToString(),
          NameValueCollection v => v.ToString(),
          JToken v => v.ToString(multiline ? Formatting.Indented : Formatting.None),
          Enum v => v.ToString(),
          _ => FormatForLog(JToken.FromObject(val), multiline, keepShort),
      };
    }

    private static void SetupLogging()
    {
      var config = new LoggingConfiguration();

      LayoutRenderer.Register("parameters", logEventInfo => {
        if (logEventInfo.Properties.ContainsKey("CallerMemberName")) logEventInfo.Properties.Remove("CallerMemberName");
        if (logEventInfo.Properties.ContainsKey("CallerFilePath")) logEventInfo.Properties.Remove("CallerFilePath");
        if (logEventInfo.Properties.ContainsKey("CallerLineNumber")) logEventInfo.Properties.Remove("CallerLineNumber");

        var parametersStr = string.Join(",", MappedDiagnosticsLogicalContext.GetNames().Select(key => (key, value: NLog.MappedDiagnosticsLogicalContext.GetObject(key)))
                                                                            .Concat(logEventInfo.Properties.Select(x => (key: (string)x.Key, value: x.Value)))
                                                                            .DistinctBy(x => x.key)
                                                                            .Select(x => $"{x.key}={FormatForLog(x.value, false, keepShort: true)}"));

        return parametersStr.Length == 0 ? "" : $"({parametersStr})";
      });

      var consoleTarget = new ConsoleTarget {
          Layout = @"[${date:format=yyyy-MM-dd HH\:mm\:ss.fff}] [${logger}] ${message} ${parameters} ${exception:format=toString}",
      };

      config.AddRuleForAllLevels(consoleTarget);

      LogManager.Configuration = config;
    }

    private static void Main()
    {
      SetupLogging();

      var server = new WebServer(LogManager.LogFactory);

      server.AddGETEndpoint("/", _ => Response.Text("OK"));

      server.AddPOSTEndpoint("/", _ => Response.Text("OK"));

      server.AddGETEndpoint("/user/<string:name>", ctx => Response.Text($"user: {ctx.Params["name"]}"));

      server.AddGETEndpoint("/data", _ => Response.Json(new { a = 1, b = 2 }));

      server.AddGETEndpoint("/stream_fixed", _ => {
        Stream ms = new MemoryStream();
        ms.WriteByte((byte)'A');
        ms.WriteByte((byte)'A');
        ms.WriteByte((byte)'A');
        ms.Position = 0;
        return Response.Stream(ms, true);
      });

      server.AddGETEndpoint("/stream_dynamic", async ctx => {
        var str = await new HttpClient().GetStreamAsync("https://httpbin.org/drip?duration=4&numbytes=20&delay=1");
        return Response.Stream(str, true);
      });

      server.AddGETEndpoint("/auth", async _ => throw new UnauthorizedException());

      server.RunSync("0.0.0.0", 8080);
    }
  }
}
using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using KDLib;
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

    static async Task Main(string[] args)
    {
      var config = new LoggingConfiguration();

      LayoutRenderer.Register("parameters", logEventInfo => {
        if (logEventInfo.Properties.ContainsKey("CallerMemberName")) logEventInfo.Properties.Remove("CallerMemberName");
        if (logEventInfo.Properties.ContainsKey("CallerFilePath")) logEventInfo.Properties.Remove("CallerFilePath");
        if (logEventInfo.Properties.ContainsKey("CallerLineNumber")) logEventInfo.Properties.Remove("CallerLineNumber");

        var parametersStr =
            NLog.MappedDiagnosticsLogicalContext.GetNames().Select(key => (key, value: NLog.MappedDiagnosticsLogicalContext.GetObject(key)))
                .Concat(logEventInfo.Properties.Select(x => (key: (string)x.Key, value: x.Value)))
                .Distinct(x => x.key)
                .Select(x => $"{x.key}={FormatForLog(x.value, false, keepShort: true)}")
                .JoinString(", ");

        return parametersStr.Length == 0 ? "" : $"({parametersStr})";
      });

      var consoleTarget = new ConsoleTarget {
          Layout = @"[${date:format=yyyy-MM-dd HH\:mm\:ss.fff}] [${logger}] ${message} ${parameters} ${exception:format=toString}",
      };

      config.AddRuleForAllLevels(consoleTarget);

      LogManager.Configuration = config;

      var server = new WebServer(LogManager.LogFactory);

      server.AddGETEndpoint("/", async ctx => Response.Text("OK"));

      server.AddPOSTEndpoint("/", async ctx => Response.Text("OK"));

      server.AddGETEndpoint("/user/<string:name>", async ctx => Response.Text($"user: {ctx.Params["name"]}"));

      server.AddGETEndpoint("/data", async ctx => Response.Json(new { a = 1, b = 2 }));

      server.AddGETEndpoint("/stream_fixed", async ctx => {
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

      server.AddGETEndpoint("/auth", async ctx => throw new UnauthorizedException());

      server.RunSync("0.0.0.0", 8080);
    }
  }
}
using System;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace KDWebServer.Example
{
  class Program
  {
    static async Task Main(string[] args)
    {
      var config = new LoggingConfiguration();

      var consoleTarget = new ConsoleTarget {
          Layout = @"[${date:format=yyyy-MM-dd HH\:mm\:ss.fff}] [${logger}] ${message} ${exception:format=toString}",
      };

      config.AddRuleForAllLevels(consoleTarget);

      LogManager.Configuration = config;

      var server = new WebServer(LogManager.LogFactory);

      server.AddGETEndpoint("/", async ctx => Response.Text("OK"));

      server.AddGETEndpoint("/user/<string:name>", async ctx => Response.Text($"user: {ctx.Params["name"]}"));

      server.AddGETEndpoint("/data", async ctx => Response.Json(new { a = 1, b = 2 }));

      await server.Run(8080);
    }
  }
}
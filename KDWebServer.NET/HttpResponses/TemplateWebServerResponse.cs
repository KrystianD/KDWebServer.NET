using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using DotLiquid;
using KDWebServer.Handlers.Http;
using NLog.Fluent;

namespace KDWebServer.HttpResponses;

public class TemplateWebServerResponse : WebServerResponse
{
  private readonly string _templateText;
  private readonly Hash? _hash;

  private TemplateWebServerResponse(string templateText, Hash? hash)
  {
    _templateText = templateText;
    _hash = hash;
  }

  internal override async Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                               Dictionary<string, object?> loggingProps)
  {
    var template = Template.Parse(_templateText);
    string html = template.Render(_hash);

    var logText = Utils.ExtractSimpleHtmlText(html);

    handler.Logger.Trace()
           .Message($"[{handler.ClientId}] sending HTML template response ({handler.ProcessingTime}ms) ({Utils.LimitText(logText, 30)})")
           .Properties(loggingProps)
           .Property("body", loggerConfig.LogPayloads ? Utils.LimitText(logText, 1000) : "<skipped>")
           .Property("status_code", StatusCode)
           .Write();

    byte[] resp = Encoding.UTF8.GetBytes(html);

    response.StatusCode = StatusCode;
    response.SendChunked = true;
    response.ContentType = "text/html";
    response.ContentLength64 = resp.LongLength;

    await response.OutputStream.WriteAsync(resp);
  }

  private static TemplateWebServerResponse FromString(string templateString, Hash? hash)
  {
    return new TemplateWebServerResponse(templateString, hash);
  }

  private static TemplateWebServerResponse FromFile(string templatePath, Hash? hash)
  {
    string templateText = File.ReadAllText(templatePath);
    return new TemplateWebServerResponse(templateText, hash);
  }

  private static async Task<TemplateWebServerResponse> FromFileAsync(string templatePath, Hash? hash)
  {
    string templateText = await File.ReadAllTextAsync(templatePath);
    return new TemplateWebServerResponse(templateText, hash);
  }

  internal static TemplateWebServerResponse FromString(string templateString) => FromString(templateString, (Hash?)null);
  internal static TemplateWebServerResponse FromString(string templateString, Dictionary<string, object> data) => FromString(templateString, Hash.FromDictionary(data));
  internal static TemplateWebServerResponse FromString(string templateString, object data) => FromString(templateString, Hash.FromAnonymousObject(data));

  internal static TemplateWebServerResponse FromFile(string templatePath) => FromFile(templatePath, (Hash?)null);
  internal static TemplateWebServerResponse FromFile(string templatePath, Dictionary<string, object> data) => FromFile(templatePath, Hash.FromDictionary(data));
  internal static TemplateWebServerResponse FromFile(string templatePath, object data) => FromFile(templatePath, Hash.FromAnonymousObject(data));

  internal static Task<TemplateWebServerResponse> FromFileAsync(string templatePath) => FromFileAsync(templatePath, (Hash?)null);
  internal static Task<TemplateWebServerResponse> FromFileAsync(string templatePath, Dictionary<string, object> data) => FromFileAsync(templatePath, Hash.FromDictionary(data));
  internal static Task<TemplateWebServerResponse> FromFileAsync(string templatePath, object data) => FromFileAsync(templatePath, Hash.FromAnonymousObject(data));
}
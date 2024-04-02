using System.Net;
using System.Text;
using DotLiquid;
using JetBrains.Annotations;
using NLog;
using HttpClientHandler = KDWebServer.Handlers.Http.HttpClientHandler;

// ReSharper disable once CheckNamespace
namespace KDWebServer.HttpResponses;

[PublicAPI]
public class TemplateWebServerResponse : WebServerResponse
{
  private readonly string _templateText;
  private readonly Hash? _hash;

  private TemplateWebServerResponse(string templateText, Hash? hash)
  {
    _templateText = templateText;
    _hash = hash;
  }

  public override async Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response, WebServerLoggerConfig loggerConfig,
                                             Dictionary<string, object?> loggingProps)
  {
    var template = Template.Parse(_templateText);
    string html = template.Render(_hash);

    var logText = WebServerUtils.ExtractSimpleHtmlText(html);

    handler.Logger.ForTraceEvent()
           .Message($"[{handler.ClientId}] sending HTML template response ({handler.HandlerTime}ms,{handler.ProcessingTime}ms) ({WebServerUtils.LimitText(logText, 30)})")
           .Properties(loggingProps)
           .Property("body", loggerConfig.LogPayloads ? WebServerUtils.LimitText(logText, 1000) : "<skipped>")
           .Property("status_code", StatusCode)
           .Log();

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

  public static TemplateWebServerResponse FromString(string templateString) => FromString(templateString, (Hash?)null);
  public static TemplateWebServerResponse FromString(string templateString, Dictionary<string, object> data) => FromString(templateString, Hash.FromDictionary(data));
  public static TemplateWebServerResponse FromString(string templateString, object data) => FromString(templateString, Hash.FromAnonymousObject(data));

  public static TemplateWebServerResponse FromFile(string templatePath) => FromFile(templatePath, (Hash?)null);
  public static TemplateWebServerResponse FromFile(string templatePath, Dictionary<string, object> data) => FromFile(templatePath, Hash.FromDictionary(data));
  public static TemplateWebServerResponse FromFile(string templatePath, object data) => FromFile(templatePath, Hash.FromAnonymousObject(data));

  public static Task<TemplateWebServerResponse> FromFileAsync(string templatePath) => FromFileAsync(templatePath, (Hash?)null);
  public static Task<TemplateWebServerResponse> FromFileAsync(string templatePath, Dictionary<string, object> data) => FromFileAsync(templatePath, Hash.FromDictionary(data));
  public static Task<TemplateWebServerResponse> FromFileAsync(string templatePath, object data) => FromFileAsync(templatePath, Hash.FromAnonymousObject(data));
}
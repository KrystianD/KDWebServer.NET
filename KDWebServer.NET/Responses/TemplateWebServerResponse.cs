using WebSocketSharp.Net;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DotLiquid;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class TemplateWebServerResponse : IWebServerResponse
  {
    private readonly string _templateText;
    private readonly Hash _hash;

    private TemplateWebServerResponse(string templateText, Hash hash)
    {
      this._templateText = templateText;
      _hash = hash;
    }

    internal override async Task WriteToResponse(WebServerClientHandler handler, HttpListenerResponse response)
    {
      var template = Template.Parse(_templateText);
      string html = template.Render(_hash);

      var logText = Utils.ExtractSimpleHtmlText(html, 1000);

      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending HTML template response ({handler.ProcessingTime}ms) ({Utils.LimitText(logText, 30)})")
             .Property("body", logText)
             .Property("status_code", StatusCode)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(html);

      response.StatusCode = StatusCode;
      response.SendChunked = true;
      response.ContentType = "text/html";
      response.ContentLength64 = resp.LongLength;

      await response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }

    private static TemplateWebServerResponse FromString(string templateString, Hash hash)
    {
      return new TemplateWebServerResponse(templateString, hash);
    }

    private static TemplateWebServerResponse FromFile(string templatePath, Hash hash)
    {
      string templateText = File.ReadAllText(templatePath);
      return new TemplateWebServerResponse(templateText, hash);
    }

#if NETCOREAPP
    private static async Task<TemplateWebServerResponse> FromFileAsync(string templatePath, Hash hash)
    {
      string templateText = await File.ReadAllTextAsync(templatePath);
      return new TemplateWebServerResponse(templateText, hash);
    }
#endif

    internal static TemplateWebServerResponse FromString(string templateString, Dictionary<string, object> data = null) => FromString(templateString, Hash.FromDictionary(data));
    internal static TemplateWebServerResponse FromString(string templateString, object data = null) => FromString(templateString, Hash.FromAnonymousObject(data));

    internal static TemplateWebServerResponse FromFile(string templatePath, Dictionary<string, object> data = null) => FromFile(templatePath, Hash.FromDictionary(data));
    internal static TemplateWebServerResponse FromFile(string templatePath, object data = null) => FromFile(templatePath, Hash.FromAnonymousObject(data));

#if NETCOREAPP
    internal static Task<TemplateWebServerResponse> FromFileAsync(string templatePath, Dictionary<string, object> data = null) => FromFileAsync(templatePath, Hash.FromDictionary(data));
    internal static Task<TemplateWebServerResponse> FromFileAsync(string templatePath, object data = null) => FromFileAsync(templatePath, Hash.FromAnonymousObject(data));
#endif
  }
}
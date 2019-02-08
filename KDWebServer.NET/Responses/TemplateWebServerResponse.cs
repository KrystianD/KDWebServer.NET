using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotLiquid;
using HtmlAgilityPack;
using NLog.Fluent;

namespace KDWebServer
{
  public class TemplateWebServerResponse : IWebServerResponse
  {
    private readonly string _templatePath;
    private Hash _hash;

    public TemplateWebServerResponse(string templatePath, Dictionary<string, object> data = null)
    {
      _templatePath = templatePath;
      _hash = Hash.FromDictionary(data ?? new Dictionary<string, object>());
    }

    public TemplateWebServerResponse(string templatePath, object data)
    {
      _templatePath = templatePath;
      _hash = Hash.FromAnonymousObject(data);
    }

    public override async Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response)
    {
      string templateText = await File.ReadAllTextAsync(_templatePath);

      var template = Template.Parse(templateText);
      string html = template.Render(_hash);

      var logText = Utils.ExtractSimpleHtmlText(html, 1000);

      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending HTML template response ({handler.ProcessingTime}ms) ({Utils.LimitText(logText, 30)})")
             .Property("body", logText)
             .Property("client_id", handler.ClientId)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(html);

      response.StatusCode = _statusCode;
      response.SendChunked = true;
      response.ContentType = "text/html";
      response.ContentLength64 = resp.LongLength;

      await response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }
  }
}
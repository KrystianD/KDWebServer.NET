using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Net;
using JetBrains.Annotations;
using KDWebServer.HttpResponses;

namespace KDWebServer;

[PublicAPI]
public static class Response
{
  public static HtmlWebServerResponse Html(string html) => new(html);

  public static JsonWebServerResponse Json(object data, bool indented = false) => new(data, indented);

  public static NotFoundWebServerResponse NotFound(string? text = null, object? json = null, string? html = null) => new(text, json, html);

  public static RedirectWebServerResponse Redirect(string location) => new(location);

  public static StatusCodeWebServerResponse StatusCode(int code) => new(code);
  public static StatusCodeWebServerResponse StatusCode(int code, string text) => new(code, text);
  public static StatusCodeWebServerResponse StatusCode(HttpStatusCode code) => new(code);
  public static StatusCodeWebServerResponse StatusCode(HttpStatusCode code, string text) => new(code, text);

  public static TemplateWebServerResponse TemplateFile(string templatePath) => TemplateWebServerResponse.FromFile(templatePath);
  public static TemplateWebServerResponse TemplateFile(string templatePath, Dictionary<string, object> data) => TemplateWebServerResponse.FromFile(templatePath, data);
  public static TemplateWebServerResponse TemplateFile(string templatePath, object data) => TemplateWebServerResponse.FromFile(templatePath, data);

  public static Task<TemplateWebServerResponse> TemplateFileAsync(string templatePath) => TemplateWebServerResponse.FromFileAsync(templatePath);
  public static Task<TemplateWebServerResponse> TemplateFileAsync(string templatePath, Dictionary<string, object> data) => TemplateWebServerResponse.FromFileAsync(templatePath, data);
  public static Task<TemplateWebServerResponse> TemplateFileAsync(string templatePath, object data) => TemplateWebServerResponse.FromFileAsync(templatePath, data);

  public static TemplateWebServerResponse TemplateString(string templateText) => TemplateWebServerResponse.FromString(templateText);
  public static TemplateWebServerResponse TemplateString(string templateText, Dictionary<string, object> data) => TemplateWebServerResponse.FromString(templateText, data);
  public static TemplateWebServerResponse TemplateString(string templateText, object data) => TemplateWebServerResponse.FromString(templateText, data);

  public static TextWebServerResponse Text(string text) => new(text);
  public static TextWebServerResponse Text(string text, string contentType) => new(text, contentType);

  public static XmlWebServerResponse Xml(string xml) => new(xml);

  public static BinaryWebServerResponse Bytes(byte[] data) => new(data);
  public static BinaryWebServerResponse Bytes(byte[] data, string mimeType) => new(data, mimeType);
  public static StreamWebServerResponse Stream(Stream stream, bool closeAfter) => new(stream, closeAfter);
  public static StreamWebServerResponse Stream(Stream stream, bool closeAfter, string mimeType) => new(stream, closeAfter, mimeType);
}
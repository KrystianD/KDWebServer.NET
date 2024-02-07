using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.IO;
using JetBrains.Annotations;
using KDWebServer.HttpResponses;

namespace KDWebServer;

[PublicAPI]
public static class Response
{
  public static HtmlWebServerResponse Html(string html) => HtmlWebServerResponse.FromString(html);

  public static JsonWebServerResponse Json(JToken data, bool indented = false) => JsonWebServerResponse.FromData(data, indented);
  public static JsonWebServerResponse Json(object data, bool indented = false) => JsonWebServerResponse.FromData(data, indented);

  public static NotFoundWebServerResponse NotFound(string? text = null, JToken? json = null, string? html = null) => NotFoundWebServerResponse.Create(text, json, html);

  public static RedirectWebServerResponse Redirect(string location) => RedirectWebServerResponse.FromLocation(location);

  public static StatusCodeWebServerResponse StatusCode(int code, string text = "") => StatusCodeWebServerResponse.FromStatusCode(code, text);
  public static StatusCodeWebServerResponse StatusCode(System.Net.HttpStatusCode code, string text = "") => StatusCodeWebServerResponse.FromStatusCode(code, text);

  public static TemplateWebServerResponse TemplateFile(string templatePath) => TemplateWebServerResponse.FromFile(templatePath);
  public static TemplateWebServerResponse TemplateFile(string templatePath, Dictionary<string, object> data) => TemplateWebServerResponse.FromFile(templatePath, data);
  public static TemplateWebServerResponse TemplateFile(string templatePath, object data) => TemplateWebServerResponse.FromFile(templatePath, data);

  public static Task<TemplateWebServerResponse> TemplateFileAsync(string templatePath) => TemplateWebServerResponse.FromFileAsync(templatePath);
  public static Task<TemplateWebServerResponse> TemplateFileAsync(string templatePath, Dictionary<string, object> data) => TemplateWebServerResponse.FromFileAsync(templatePath, data);
  public static Task<TemplateWebServerResponse> TemplateFileAsync(string templatePath, object data) => TemplateWebServerResponse.FromFileAsync(templatePath, data);

  public static TemplateWebServerResponse TemplateString(string templateText) => TemplateWebServerResponse.FromString(templateText);
  public static TemplateWebServerResponse TemplateString(string templateText, Dictionary<string, object> data) => TemplateWebServerResponse.FromString(templateText, data);
  public static TemplateWebServerResponse TemplateString(string templateText, object data) => TemplateWebServerResponse.FromString(templateText, data);

  public static TextWebServerResponse Text(string text) => TextWebServerResponse.FromString(text);
  public static TextWebServerResponse Text(string text, string contentType) => TextWebServerResponse.FromString(text, contentType);

  public static XmlWebServerResponse Xml(string xml) => XmlWebServerResponse.FromString(xml);

  public static BinaryWebServerResponse Bytes(byte[] data, string mimeType = "application/octet-stream") => BinaryWebServerResponse.FromBytes(data, mimeType);
  public static StreamWebServerResponse Stream(Stream stream, bool closeAfter, string mimeType = "application/octet-stream") => StreamWebServerResponse.FromStream(stream, closeAfter, mimeType);
}
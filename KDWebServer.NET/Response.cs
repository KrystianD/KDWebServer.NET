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
  public static HTMLWebServerResponse Html(string html) => HTMLWebServerResponse.FromString(html);

  public static JSONWebServerResponse Json(JToken data, bool indented = false) => JSONWebServerResponse.FromData(data, indented);
  public static JSONWebServerResponse Json(object data, bool indented = false) => JSONWebServerResponse.FromData(data, indented);

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

  public static XMLWebServerResponse Xml(string xml) => XMLWebServerResponse.FromString(xml);

  public static BinaryWebServerResponse Bytes(byte[] data, string mimeType = "application/octet-stream") => BinaryWebServerResponse.FromBytes(data, mimeType);
  public static StreamWebServerResponse Stream(Stream stream, bool closeAfter, string mimeType = "application/octet-stream") => StreamWebServerResponse.FromStream(stream, closeAfter, mimeType);
}
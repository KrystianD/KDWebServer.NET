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

  public static TextWebServerResponse Text(string text) => new(text);
  public static TextWebServerResponse Text(string text, string contentType) => new(text, contentType);

  public static XmlWebServerResponse Xml(string xml) => new(xml);

  public static BinaryWebServerResponse Bytes(byte[] data) => new(data);
  public static BinaryWebServerResponse Bytes(byte[] data, string mimeType) => new(data, mimeType);
  public static StreamWebServerResponse Stream(Stream stream, bool closeAfter) => new(stream, closeAfter);
  public static StreamWebServerResponse Stream(Stream stream, bool closeAfter, string mimeType) => new(stream, closeAfter, mimeType);
}
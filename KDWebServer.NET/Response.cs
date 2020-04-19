using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using KDWebServer.Responses;
using Newtonsoft.Json.Linq;

namespace KDWebServer
{
  public static class Response
  {
    public static HTMLWebServerResponse Html(string html) => HTMLWebServerResponse.FromString(html);

    public static JSONWebServerResponse Json(JToken data) => JSONWebServerResponse.FromData(data);
    public static JSONWebServerResponse Json(object data) => JSONWebServerResponse.FromData(data);

    public static NotFoundWebServerResponse NotFound() => NotFoundWebServerResponse.Create();

    public static RedirectWebServerResponse Redirect(string location) => RedirectWebServerResponse.FromLocation(location);

    public static StatusCodeWebServerResponse StatusCode(int code) => StatusCodeWebServerResponse.FromStatusCode(code);
    public static StatusCodeWebServerResponse StatusCode(HttpStatusCode code) => StatusCodeWebServerResponse.FromStatusCode(code);

    public static TemplateWebServerResponse TemplateFile(string templatePath, Dictionary<string, object> data = null) => TemplateWebServerResponse.FromFile(templatePath, data);
    public static TemplateWebServerResponse TemplateFile(string templatePath, object data = null) => TemplateWebServerResponse.FromFile(templatePath, data);

#if NETCOREAPP
    public static Task<TemplateWebServerResponse> TemplateFileAsync(string templatePath, Dictionary<string, object> data = null) => TemplateWebServerResponse.FromFileAsync(templatePath, data);
    public static Task<TemplateWebServerResponse> TemplateFileAsync(string templatePath, object data = null) => TemplateWebServerResponse.FromFileAsync(templatePath, data);
#endif 
    
    public static TemplateWebServerResponse TemplateString(string templateText, Dictionary<string, object> data = null) => TemplateWebServerResponse.FromString(templateText, data);
    public static TemplateWebServerResponse TemplateString(string templateText, object data = null) => TemplateWebServerResponse.FromString(templateText, data);

    public static TextWebServerResponse Text(string text) => TextWebServerResponse.FromString(text);
    public static TextWebServerResponse Text(string text, string contentType) => TextWebServerResponse.FromString(text, contentType);

    public static XMLWebServerResponse Xml(string xml) => XMLWebServerResponse.FromString(xml);
  }
}
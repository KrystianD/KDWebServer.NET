using WebSocketSharp.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class JSONWebServerResponse : IWebServerResponse
  {
    private readonly string _json;

    private JSONWebServerResponse(string json)
    {
      _json = json;
    }

    internal override Task WriteToResponse(WebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending JSON response ({handler.ProcessingTime}ms)")
             .Property("data", _json)
             .Property("code", StatusCode)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(_json);

      response.StatusCode = StatusCode;
      response.SendChunked = true;
      response.ContentType = "application/json";
      response.ContentLength64 = resp.LongLength;

      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }

    internal static JSONWebServerResponse FromData(JToken data) => new JSONWebServerResponse(data.ToString(Formatting.None));
    internal static JSONWebServerResponse FromData(object data) => FromData(JToken.FromObject(data));
  }
}
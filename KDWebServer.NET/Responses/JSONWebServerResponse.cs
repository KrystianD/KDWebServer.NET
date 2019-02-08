using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog.Fluent;

namespace KDWebServer
{
  public class JSONWebServerResponse : IWebServerResponse
  {
    private readonly string _json;

    public JSONWebServerResponse(JToken data)
    {
      _json = data.ToString(Formatting.None);
    }

    public JSONWebServerResponse(object data)
    {
      _json = JToken.FromObject(data).ToString(Formatting.None);
    }

    public override Task WriteToResponse(InternalWebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending JSON response ({handler.ProcessingTime}ms)")
             .Property("data", _json)
             .Property("client_id", handler.ClientId)
             .Write();

      byte[] resp = Encoding.UTF8.GetBytes(_json);

      response.StatusCode = _statusCode;
      response.SendChunked = true;
      response.ContentType = "application/json";
      response.ContentLength64 = resp.LongLength;

      return response.OutputStream.WriteAsync(resp, 0, resp.Length);
    }
  }
}
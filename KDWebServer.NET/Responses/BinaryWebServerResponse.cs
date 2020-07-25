using WebSocketSharp.Net;
using System.Threading.Tasks;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class BinaryWebServerResponse : IWebServerResponse
  {
    private readonly byte[] _data;
    private readonly string _mimeType;

    private BinaryWebServerResponse(byte[] data, string mimeType = "application/octet-stream")
    {
      _data = data;
      _mimeType = mimeType;
    }

    internal override Task WriteToResponse(WebServerClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Info()
             .Message($"[{handler.ClientId}] sending binary response ({handler.ProcessingTime}ms) ({Utils.BytesToString(_data.Length)})")
             .Property("data_length", _data.Length)
             .Property("code", StatusCode)
             .Property("client_id", handler.ClientId)
             .Write();

      response.StatusCode = StatusCode;
      response.SendChunked = true;
      response.ContentType = _mimeType;
      response.ContentLength64 = _data.LongLength;

      return response.OutputStream.WriteAsync(_data, 0, _data.Length);
    }

    internal static BinaryWebServerResponse FromBytes(byte[] data, string mimeType = "application/octet-stream") => new BinaryWebServerResponse(data, mimeType);
  }
}
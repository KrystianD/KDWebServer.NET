using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog.Fluent;
using WebSocketSharp.Net;

namespace KDWebServer.HttpResponses
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

    internal override Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response)
    {
      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending binary response ({handler.ProcessingTime}ms) ({Utils.BytesToString(_data.Length)})")
             .Property("data_length", _data.Length)
             .Property("status_code", StatusCode)
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
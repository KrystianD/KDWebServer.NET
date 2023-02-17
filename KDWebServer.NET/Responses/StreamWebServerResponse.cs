using WebSocketSharp.Net;
using System.IO;
using System.Threading.Tasks;
using KDWebServer.Handlers.Http;
using NLog.Fluent;

namespace KDWebServer.Responses
{
  public class StreamWebServerResponse : IWebServerResponse
  {
    private readonly Stream _stream;
    private readonly bool _closeAfter;
    private readonly string _mimeType;

    private StreamWebServerResponse(Stream stream, bool closeAfter, string mimeType = "application/octet-stream")
    {
      _stream = stream;
      _closeAfter = closeAfter;
      _mimeType = mimeType;
    }

    internal override async Task WriteToResponse(HttpClientHandler handler, HttpListenerResponse response)
    {
      long lengthToSend = -1;
      var lengthToSendStr = "unknown length";
      if (_stream.CanSeek) {
        var curPos = _stream.Position;
        var endPos = _stream.Seek(0, SeekOrigin.End);
        _stream.Seek(curPos, SeekOrigin.Begin);
        lengthToSend = endPos - curPos;
        lengthToSendStr = Utils.BytesToString(lengthToSend);
      }

      handler.Logger.Trace()
             .Message($"[{handler.ClientId}] sending stream response ({handler.ProcessingTime}ms) ({lengthToSendStr})")
             .Property("status_code", StatusCode)
             .Write();

      response.StatusCode = StatusCode;
      response.SendChunked = true;
      response.ContentType = _mimeType;

      if (lengthToSend != -1)
        response.ContentLength64 = lengthToSend;

      await _stream.CopyToAsync(response.OutputStream);
      if (_closeAfter)
        _stream.Close();
    }

    internal static StreamWebServerResponse FromStream(Stream stream, bool closeAfter, string mimeType = "application/octet-stream") => new StreamWebServerResponse(stream, closeAfter, mimeType);
  }
}
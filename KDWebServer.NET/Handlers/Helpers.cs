using System.Net;
using System.Text;

namespace KDWebServer.Handlers;

internal static class Helpers
{
  public static void SetResponse(HttpListenerResponse response, int errorCode, string? errorMessage = null)
  {
    try {
      response.StatusCode = errorCode;

      if (errorMessage != null) {
        byte[] resp = Encoding.UTF8.GetBytes(errorMessage);
        response.ContentType = "text/plain";
        response.OutputStream.Write(resp, 0, resp.Length);
      }
    }
    catch { // ignored
    }
  }

  public static void CloseStream(HttpListenerResponse response, int? errorCode = null, string? errorMessage = null)
  {
    try {
      if (errorCode.HasValue)
        SetResponse(response, errorCode.Value, errorMessage);

      response.OutputStream.Close();
    }
    catch { // ignored
    }
  }
}
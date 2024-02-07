using System.Net;
using System.Text;

namespace KDWebServer.Handlers;

internal static class Helpers
{
  public static void CloseStream(HttpListenerResponse response, int? errorCode = null, string? errorMessage = null)
  {
    try {
      if (errorCode.HasValue) {
        response.StatusCode = errorCode.Value;
      }

      if (errorMessage != null) {
        byte[] resp = Encoding.UTF8.GetBytes(errorMessage);
        response.ContentType = "text/plain";
        response.OutputStream.Write(resp, 0, resp.Length);
      }

      response.OutputStream.Close();
    }
    catch { // ignored
    }
  }
}
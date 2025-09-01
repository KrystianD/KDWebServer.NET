using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace KDWebServer.Handlers;

internal static class Helpers
{
  public static void SetResponse(HttpResponse response, int errorCode, string? errorMessage = null)
  {
    try {
      response.StatusCode = errorCode;

      if (errorMessage != null) {
        byte[] resp = Encoding.UTF8.GetBytes(errorMessage);
        response.ContentType = "text/plain";
        response.Body.Write(resp, 0, resp.Length);
      }
    }
    catch { // ignored
    }
  }

  public static void CloseStream(HttpResponse response, int? errorCode = null, string? errorMessage = null)
  {
    try {
      if (errorCode.HasValue)
        SetResponse(response, errorCode.Value, errorMessage);

      response.CompleteAsync();
    }
    catch { // ignored
    }
  }
}
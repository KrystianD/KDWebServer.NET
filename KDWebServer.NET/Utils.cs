using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace KDWebServer
{
  internal static class Utils
  {
    public static string ExtractSimpleHtmlText(string html, int maxLength)
    {
      var e = new HtmlDocument();
      e.LoadHtml(html);

      HtmlNode node = e.DocumentNode.SelectSingleNode("//body") ?? e.DocumentNode;

      var text = node.InnerText;
      if (string.IsNullOrWhiteSpace(text))
        return "<no-text>";
      text = Regex.Replace(text, @"[ \t\n\r]+", " ").Trim();
      if (text.Length > maxLength)
        text = text.Substring(0, maxLength - 3) + "...";
      return text;
    }


    public static IPAddress GetClientIp(HttpListenerContext httpContext, HashSet<IPAddress> trustedProxies = null)
    {
      return GetClientIp(httpContext.Request.RemoteEndPoint?.Address,
                         httpContext.Request.Headers["X-Forwarded-For"]?.Split(','),
                         httpContext.Request.Headers["X-Real-IP"],
                         trustedProxies);
    }

    private static IPAddress GetClientIp(IPAddress clientIp, IReadOnlyList<string> xForwardedFor, string realIp, HashSet<IPAddress> trustedProxies = null)
    {
      if (trustedProxies != null && trustedProxies.Contains(clientIp)) {
        if (xForwardedFor != null)
          return IPAddress.Parse(xForwardedFor[0]);
        else if (realIp != null)
          return IPAddress.Parse(realIp);
      }

      return clientIp;
    }

    public static string LimitText(string text, int maxLength)
    {
      if (text == null)
        return "(null)";
      if (text.Length > maxLength - 3)
        text = text.Substring(0, maxLength - 3) + "...";
      return text;
    }
  }
}
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using HttpListenerContext = WebSocketSharp.Net.HttpListenerContext;

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

    public static string BytesToString(long byteCount)
    {
      string[] suf = { "B", "KB", "MB", "GB", "TB" };
      if (byteCount == 0)
        return "0" + suf[0];
      long bytes = Math.Abs(byteCount);
      int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
      double num = Math.Round(bytes / Math.Pow(1024, place), 1);
      return $"{Math.Sign(byteCount) * num:0.##} {suf[place]}";
    }
  }
}
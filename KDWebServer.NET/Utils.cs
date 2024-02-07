using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace KDWebServer;

internal static class Utils
{
  public static TOut Let<TIn, TOut>(this TIn value, Func<TIn, TOut> f) => f(value);

  public static string ExtractSimpleHtmlText(string html)
  {
    var e = new HtmlDocument();
    e.LoadHtml(html);

    HtmlNode node = e.DocumentNode.SelectSingleNode("//body") ?? e.DocumentNode;

    var text = node.InnerText;
    return string.IsNullOrWhiteSpace(text)
        ? "<no-text>"
        : Regex.Replace(text, @"[ \t\n\r]+", " ").Trim();
  }


  public static IPAddress? GetClientIp(HttpListenerContext httpContext, HashSet<IPAddress>? trustedProxies = null)
  {
    // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
    return GetClientIp(httpContext.Request.RemoteEndPoint?.Address,
                       httpContext.Request.Headers["X-Forwarded-For"]?.Split(','),
                       httpContext.Request.Headers["X-Real-IP"],
                       trustedProxies);
  }

  private static IPAddress? GetClientIp(IPAddress? clientIp, IReadOnlyList<string>? xForwardedFor, string? realIp, HashSet<IPAddress>? trustedProxies = null)
  {
    if (trustedProxies != null && clientIp != null && trustedProxies.Contains(clientIp)) {
      if (xForwardedFor != null)
        return IPAddress.Parse(xForwardedFor[0]);
      else if (realIp != null)
        return IPAddress.Parse(realIp);
    }

    return clientIp;
  }

  public static string LimitText(string? text, int maxLength)
  {
    if (text == null)
      return "(null)";
    if (text.Length > maxLength - 3)
      text = text[..(maxLength - 3)] + "...";
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

  public static X509Certificate2 LoadPemCertificate(string certificatePath, string privateKeyPath)
  {
    using var publicKey = new X509Certificate2(certificatePath);

    var privateKeyText = File.ReadAllText(privateKeyPath);
    var privateKeyBlocks = privateKeyText.Split("-", StringSplitOptions.RemoveEmptyEntries);
    var privateKeyBytes = Convert.FromBase64String(privateKeyBlocks[1]);
    using var rsa = RSA.Create();

    switch (privateKeyBlocks[0]) {
      case "BEGIN PRIVATE KEY":
        rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
        break;
      case "BEGIN RSA PRIVATE KEY":
        rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
        break;
      default:
        throw new ArgumentException("Invalid private key PEM file");
    }

    var keyPair = publicKey.CopyWithPrivateKey(rsa);
    return new X509Certificate2(keyPair.Export(X509ContentType.Pfx));
  }

  private static readonly Random StringRandom = new();

  public static string GenerateRandomString(
      int length,
      string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
  {
    var stringChars = new char[length];

    lock (StringRandom) {
      for (var i = 0; i < stringChars.Length; i++)
        stringChars[i] = chars[StringRandom.Next(chars.Length)];
    }

    return new string(stringChars);
  }
}
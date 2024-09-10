using Newtonsoft.Json;

namespace KDWebServer;

internal static class Consts
{
  public const string DefaultDateTimeFormat = "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ";

  public static readonly JsonSerializerSettings DefaultSerializerSettings = new() {
      DateFormatString = DefaultDateTimeFormat,
  };

  public static readonly JsonSerializer DefaultSerializer = JsonSerializer.Create(DefaultSerializerSettings);
}
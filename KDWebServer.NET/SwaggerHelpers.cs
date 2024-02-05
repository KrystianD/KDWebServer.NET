using System.Web;

namespace KDWebServer;

internal static class SwaggerHelpers
{
  public static string GenerateSwaggerHtml(string openApiJsonUrl, string? name)
  {
    var title = name?.Let(x => x + " - Swagger UI") ?? "SwaggerUI";
    var description = name ?? "SwaggerUI";

    var html = @"<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <meta
    name=""description""
    content=""%DESCRIPTION%""
  />
  <title>%TITLE%</title>
  <link rel=""shortcut icon"" href=""https://fastapi.tiangolo.com/img/favicon.png"" />
  <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/swagger-ui-dist@5.9.0/swagger-ui.css"" />
</head>
<body>
<div id=""swagger-ui""></div>
<script src=""https://cdn.jsdelivr.net/npm/swagger-ui-dist@5.9.0/swagger-ui-bundle.js"" crossorigin></script>
<script>
  window.onload = () => {
    window.ui = SwaggerUIBundle({
      url: '" + openApiJsonUrl + @"',
      dom_id: '#swagger-ui',
      layout: ""BaseLayout"",
      deepLinking: true,
      showExtensions: true,
      showCommonExtensions: true,
      presets: [
        SwaggerUIBundle.presets.apis,
        SwaggerUIBundle.SwaggerUIStandalonePreset
      ],
    });
  };
</script>
</body>
</html>";

    html = html.Replace("%TITLE%", HttpUtility.HtmlEncode(title));
    html = html.Replace("%DESCRIPTION%", HttpUtility.HtmlEncode(description));

    return html;
  }
}
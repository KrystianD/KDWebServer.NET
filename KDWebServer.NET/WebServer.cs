using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading.Tasks;
using JetBrains.Annotations;
using KDWebServer.Handlers;
using KDWebServer.Handlers.Http;
using KDWebServer.Handlers.Websocket;
using NLog;
using NSwag;

namespace KDWebServer
{
  [PublicAPI]
  public class WebServerSslConfig
  {
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.Tls12;
    public string CertificatePath { get; set; }
    public string KeyPath { get; set; }
    public bool ClientCertificateRequired { get; set; } = false;
    public RemoteCertificateValidationCallback ClientCertificateValidationCallback { get; set; } = (sender, certificate, chain, sslPolicyErrors) => true;
  }

  [PublicAPI]
  public class WebServerLoggerConfig
  {
    public bool LogPayloads = true;
  }

  [PublicAPI]
  public class WebServer
  {
    public delegate Task<IWebServerResponse> AsyncEndpointHandler(HttpRequestContext ctx);

    public delegate IWebServerResponse EndpointHandler(HttpRequestContext ctx);

    public delegate Task AsyncWebsocketEndpointHandler(WebsocketRequestContext ctx);

    private HttpListener _listener;

    internal class EndpointDefinition
    {
      public readonly string Endpoint;
      public readonly AsyncEndpointHandler HttpCallback;
      public readonly AsyncWebsocketEndpointHandler WsCallback;
      public readonly bool SkipDocs;

      public bool IsWebsocket => WsCallback != null;

      public EndpointDefinition(string endpoint, AsyncEndpointHandler httpCallback, AsyncWebsocketEndpointHandler wsCallback, bool skipDocs)
      {
        Endpoint = endpoint;
        HttpCallback = httpCallback;
        WsCallback = wsCallback;
        SkipDocs = skipDocs;
      }
    }

    public string? Name { get; set; }

    internal NLog.LogFactory LogFactory { get; }
    internal WebServerLoggerConfig LoggerConfig { get; }

    private readonly NLog.ILogger _logger;

    internal readonly Dictionary<Router.RouteDescriptor, EndpointDefinition> Endpoints = new Dictionary<Router.RouteDescriptor, EndpointDefinition>();
    internal HashSet<IPAddress> TrustedProxies;

    public int WebsocketSenderQueueLength = 10;

    public WebServer(NLog.LogFactory factory, [CanBeNull] WebServerLoggerConfig loggerConfig = null)
    {
      LogFactory = factory;
      LoggerConfig = loggerConfig ?? new WebServerLoggerConfig();
      _logger = factory?.GetLogger("webserver") ?? LogManager.LogFactory.CreateNullLogger();
    }

    public void SetTrustedProxies(IEnumerable<IPAddress> trustedProxies)
    {
      TrustedProxies = trustedProxies.ToHashSet();
    }

    public void AddEndpoint(string endpoint, EndpointHandler callback, HashSet<HttpMethod> methods, bool skipDocs = false) => AddEndpoint(endpoint, ctx => Task.FromResult(callback(ctx)), methods, skipDocs);

    public void AddEndpoint(string endpoint, AsyncEndpointHandler callback, HashSet<HttpMethod> methods, bool skipDocs = false)
    {
      if (!(endpoint.StartsWith("/") || endpoint == "*"))
        throw new ArgumentException("Endpoint path must start with slash or be a catch-all one (*)");

      var route = Router.CompileRoute(endpoint);
      route.Methods = methods;
      Endpoints.Add(route, new EndpointDefinition(endpoint, callback, null, skipDocs));
    }

    public void AddWsEndpoint(string endpoint, AsyncWebsocketEndpointHandler callback)
    {
      if (!(endpoint.StartsWith("/") || endpoint == "*"))
        throw new ArgumentException("Endpoint path must start with slash or be a catch-all one (*)");

      var route = Router.CompileRoute(endpoint);
      route.Methods = new HashSet<HttpMethod>() { HttpMethod.Get };
      Endpoints.Add(route, new EndpointDefinition(endpoint, null, callback, false));
    }

    public void AddGETEndpoint(string endpoint, EndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Get });
    public void AddGETEndpoint(string endpoint, AsyncEndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Get });
    public void AddPOSTEndpoint(string endpoint, EndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Post });
    public void AddPOSTEndpoint(string endpoint, AsyncEndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Post });

    public void AddSwaggerEndpoint(string endpoint)
    {
      var openApiDocument = new OpenApiDocument();
      
      if (Name != null)
        openApiDocument.Info.Title = Name;

      foreach (var (route, definition) in Endpoints) {
        if (definition.SkipDocs)
          continue;

        var item = new OpenApiPathItem();
        foreach (var method in route.Methods) {
          var op = new OpenApiOperation();
          foreach (var (_, parameterDescriptor) in route.Params) {
            op.Parameters.Add(parameterDescriptor.OpenApiParameter);
          }

          if (definition.IsWebsocket) {
            op.Summary = "websocket";
          }

          item.Add(method.Method, op);
        }

        openApiDocument.Paths[route.OpanApiPath] = item;
      }

      var schemaJson = openApiDocument.ToJson();

      AddGETEndpoint(endpoint, _ => Response.Html(SwaggerHelpers.GenerateSwaggerHtml(endpoint + "/openapi.json", name: Name)));
      AddGETEndpoint(endpoint + "/openapi.json", _ => Response.Text(schemaJson));
    }

    public void RunSync(string host, int port, WebServerSslConfig sslConfig = null)
    {
      Start(host, port, sslConfig);
      InternalRun().Wait();
    }

    public void RunAsync(string host, int port, WebServerSslConfig sslConfig = null)
    {
      Start(host, port, sslConfig);
      var _ = InternalRun();
    }

    private void Start(string host, int port, WebServerSslConfig sslConfig)
    {
      _listener = new HttpListener();

      if (sslConfig == null) {
        _logger.Info($"Starting HTTP server on {host}:{port}");
        _listener.Prefixes.Add($"http://*:{port}/");
      }
      else {
        throw new ArgumentException("ssl not supported");
      }

      _listener.Start();
    }

    [SuppressMessage("ReSharper", "FunctionNeverReturns")]
    private async Task InternalRun()
    {
      while (true) {
        HttpListenerContext httpContext = null;
        try {
          httpContext = await Task.Factory.FromAsync(_listener.BeginGetContext, _listener.EndGetContext, null);

          var rq = new RequestDispatcher(this);
          rq.DispatchRequest(httpContext);
        }
        catch (Exception e) {
          _logger.Error(e, "An error occurred during handling webserver client");
          if (httpContext != null) {
            httpContext.Response.StatusCode = 500;
            httpContext.Response.Close();
          }
        }
      }
    }
  }
}
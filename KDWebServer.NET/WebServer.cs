using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using KDWebServer.Handlers;
using KDWebServer.Handlers.Http;
using KDWebServer.Handlers.Websocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NJsonSchema;
using NLog;
using NSwag;
using ILogger = NLog.ILogger;

namespace KDWebServer;

[PublicAPI]
public class WebServerSslConfig
{
  public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.Tls12;
  public string? CertificatePath { get; set; }
  public string? KeyPath { get; set; }
  public bool ClientCertificateRequired { get; set; } = false;
  public RemoteCertificateValidationCallback ClientCertificateValidationCallback { get; set; } = (_, _, _, _) => true;
}

[PublicAPI]
public class WebServerConfig
{
  public WebServerRouterConfig Router = new();
  public WebServerLoggerConfig Logger = new();
}

[PublicAPI]
public class WebServerRouterConfig
{
  public bool AllowTrailingSlash = false;
  public bool AllowDuplicatedSlashes = false;
}

[PublicAPI]
public class WebServerLoggerConfig
{
  public bool LogPayloads = true;
}

[PublicAPI]
public class WebServer
{
  public delegate Task<WebServerResponse> AsyncEndpointHandler(HttpRequestContext ctx);
  public delegate Task<WebServerResponse> AsyncEndpointHandlerWithCancellation(HttpRequestContext ctx, CancellationToken token);

  public delegate WebServerResponse EndpointHandler(HttpRequestContext ctx);
  public delegate WebServerResponse EndpointHandlerWithCancellation(HttpRequestContext ctx, CancellationToken token);

  public delegate Task AsyncWebsocketEndpointHandler(WebsocketRequestContext ctx, CancellationToken token);

  private WebApplication _listener;

  public class EndpointDefinition
  {
    public readonly string Endpoint;
    public readonly AsyncEndpointHandlerWithCancellation? HttpCallback;
    public readonly AsyncWebsocketEndpointHandler? WsCallback;
    public readonly HashSet<HttpMethod> Methods;
    public readonly bool SkipDocs;
    public readonly Action<OpenApiOperation> DocsCreator;
    public readonly bool RunOnThreadPool;

    public bool IsWebsocket => WsCallback != null;

    public EndpointDefinition(string endpoint,
                              AsyncEndpointHandlerWithCancellation? httpCallback,
                              AsyncWebsocketEndpointHandler? wsCallback,
                              HashSet<HttpMethod> methods,
                              bool skipDocs,
                              Action<OpenApiOperation>? docsCreator,
                              bool runOnThreadPool)
    {
      Endpoint = endpoint;
      HttpCallback = httpCallback;
      WsCallback = wsCallback;
      Methods = methods;
      SkipDocs = skipDocs;
      DocsCreator = docsCreator ?? (_ => { });
      RunOnThreadPool = runOnThreadPool;
    }
  }

  public string? Name { get; set; }
  public List<IRequestObserver> Observers { get; } = new();

  internal LogFactory? LogFactory { get; }
  internal SynchronizationContext? SynchronizationContext { get; }
  internal WebServerConfig Config { get; }

  private readonly ILogger _logger;

  internal readonly List<(Router.RouteDescriptor, EndpointDefinition)> Endpoints = new();
  internal HashSet<IPAddress>? TrustedProxies;

  public int WebsocketSenderQueueLength = 10;

  private CancellationTokenSource? _serverShutdownTokenSource;
  internal CancellationToken ServerShutdownToken => _serverShutdownTokenSource!.Token;

  private OpenApiDocument _openApiDocument = new() {
      SchemaType = SchemaType.OpenApi3,
  };

  public WebServer(LogFactory? factory, WebServerConfig? config = null, SynchronizationContext? synchronizationContext = null)
  {
    config ??= new WebServerConfig();

    LogFactory = factory;
    SynchronizationContext = synchronizationContext;
    Config = config;
    _logger = factory?.GetLogger("webserver") ?? LogManager.LogFactory.CreateNullLogger();
  }

  public void SetTrustedProxies(IEnumerable<IPAddress> trustedProxies)
  {
    TrustedProxies = trustedProxies.ToHashSet();
  }

  public void AddEndpoint(string endpoint, EndpointHandler callback, HashSet<HttpMethod> methods, bool skipDocs = false, Action<OpenApiOperation>? docsCreator = null, bool runOnThreadPool = false)
  {
    AddEndpoint(endpoint, ctx => Task.FromResult(callback(ctx)), methods, skipDocs, docsCreator, runOnThreadPool);
  }

  public void AddEndpoint(string endpoint, AsyncEndpointHandler callback, HashSet<HttpMethod> methods, bool skipDocs = false, Action<OpenApiOperation>? docsCreator = null, bool runOnThreadPool = false)
  {
    AddEndpoint(endpoint, (x, _) => callback(x), methods, skipDocs, docsCreator, runOnThreadPool);
  }

  public void AddEndpoint(string endpoint, AsyncEndpointHandlerWithCancellation callback, HashSet<HttpMethod> methods, bool skipDocs = false, Action<OpenApiOperation>? docsCreator = null, bool runOnThreadPool = false)
  {
    if (!(endpoint.StartsWith("/") || endpoint == "*"))
      throw new ArgumentException("endpoint path must start with slash or be a catch-all one (*)");

    var route = Router.CompileRoute(endpoint, Config.Router);
    Endpoints.Add((route, new EndpointDefinition(endpoint, callback, null, methods, skipDocs, docsCreator, runOnThreadPool)));
  }

  public void AddWsEndpoint(string endpoint, AsyncWebsocketEndpointHandler callback, bool skipDocs = false, Action<OpenApiOperation>? docsCreator = null, bool runOnThreadPool = false)
  {
    if (!(endpoint.StartsWith("/") || endpoint == "*"))
      throw new ArgumentException("endpoint path must start with slash or be a catch-all one (*)");

    var route = Router.CompileRoute(endpoint, Config.Router);
    var methods = new HashSet<HttpMethod>() { HttpMethod.Get };
    Endpoints.Add((route, new EndpointDefinition(endpoint, null, callback, methods, skipDocs, docsCreator, runOnThreadPool)));
  }

  public void AddGETEndpoint(string endpoint, EndpointHandler callback, Action<OpenApiOperation>? docsCreator = null) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Get }, skipDocs: false, docsCreator);
  public void AddGETEndpoint(string endpoint, AsyncEndpointHandler callback, Action<OpenApiOperation>? docsCreator = null) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Get }, skipDocs: false, docsCreator);
  public void AddPOSTEndpoint(string endpoint, EndpointHandler callback, Action<OpenApiOperation>? docsCreator = null) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Post }, skipDocs: false, docsCreator);
  public void AddPOSTEndpoint(string endpoint, AsyncEndpointHandler callback, Action<OpenApiOperation>? docsCreator = null) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Post }, skipDocs: false, docsCreator);

  public void AppendSwaggerDocument(OpenApiDocument doc)
  {
    foreach (var (name, jsonSchema) in doc.Definitions) {
      if (!_openApiDocument.Definitions.TryAdd(name, jsonSchema)) {
        if (!ReferenceEquals(jsonSchema, _openApiDocument.Definitions[name])) {
          jsonSchema.Reference = _openApiDocument.Definitions[name];
        }
      }
    }

    foreach (var (path, newPathItem) in doc.Paths) {
      if (!_openApiDocument.Paths.TryGetValue(path, out var pathItem)) {
        pathItem = new OpenApiPathItem();
        _openApiDocument.Paths[path] = pathItem;
      }

      foreach (var (method, openApiOperation) in newPathItem) {
        if (!pathItem.TryAdd(method, openApiOperation)) {
          throw new ArgumentException($"duplicated {method} on {path}");
        }
      }
    }
  }

  public void AddSwaggerEndpoint(string endpoint)
  {
    if (Name != null)
      _openApiDocument.Info.Title = Name;

    foreach (var (route, definition) in Endpoints) {
      if (definition.SkipDocs)
        continue;

      OpenApiPathItem? item;
      if (!_openApiDocument.Paths.TryGetValue(route.OpanApiPath, out item)) {
        item = new OpenApiPathItem();
        _openApiDocument.Paths[route.OpanApiPath] = item;
      }

      foreach (var method in definition.Methods) {
        var op = new OpenApiOperation();
        foreach (var (name, typeConverter) in route.Params) {
          var openApiParameter = new OpenApiParameter {
              Name = name,
              Kind = OpenApiParameterKind.Path,
              IsRequired = true,
          };
          typeConverter.ApplyToJsonSchema(openApiParameter);
          op.Parameters.Add(openApiParameter);
        }

        if (definition.IsWebsocket) {
          op.Summary = "websocket";
        }

        definition.DocsCreator(op);

        item.Add(method.Method, op);
      }
    }

    var schemaJson = _openApiDocument.ToJson();

    AddGETEndpoint(endpoint, _ => Response.Html(SwaggerHelpers.GenerateSwaggerHtml("./docs/openapi.json", name: Name)));
    AddGETEndpoint(endpoint + "/openapi.json", _ => Response.Text(schemaJson));
  }

  public class NoopConsoleLifetime : IHostLifetime
  {
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  }

  public void Start(string host, int port)
  {
    var builder = WebApplication.CreateBuilder();
    builder.Services.AddLogging(x => x.ClearProviders());
    builder.Services.AddSingleton<IHostLifetime, NoopConsoleLifetime>();

    builder.Services.Configure<KestrelServerOptions>(options => {
      options.AllowSynchronousIO = true;
    });

    builder.WebHost.UseKestrel(options => {
      options.Listen(IPAddress.Any, port);
    });
    _listener = builder.Build();

    _listener.UseWebSockets(new WebSocketOptions() {
    });
    _listener.Use(async (HttpContext context, Func<Task> next) => {
      await HandlerRequest(context);
    });

    _logger.Info($"Starting HTTP server on http://{host}:{port}");

    _serverShutdownTokenSource = new();
    _listener.RunAsync(_serverShutdownTokenSource.Token);
  }

  public void Stop()
  {
    _listener?.StopAsync();
    _serverShutdownTokenSource?.Cancel();
  }

  [SuppressMessage("ReSharper", "FunctionNeverReturns")]
  private async Task HandlerRequest(HttpContext httpContext)
  {
    try {
      var connectionTime = DateTime.UtcNow;
      var requestTimer = Stopwatch.StartNew();

      var rq = new RequestDispatcher(this);
      await rq.DispatchRequest(httpContext, connectionTime, requestTimer);
    }
    catch (Exception e) {
      _logger.Error(e, "An error occurred during handling webserver client");
      httpContext.Response.StatusCode = 500;
      await httpContext.Response.CompleteAsync();
    }
  }
}
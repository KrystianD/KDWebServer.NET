﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using NJsonSchema;
using NLog;
using NSwag;

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
public class WebServerLoggerConfig
{
  public bool LogPayloads = true;
}

[PublicAPI]
public class WebServer
{
  public delegate Task<WebServerResponse> AsyncEndpointHandler(HttpRequestContext ctx);

  public delegate WebServerResponse EndpointHandler(HttpRequestContext ctx);

  public delegate Task AsyncWebsocketEndpointHandler(WebsocketRequestContext ctx, CancellationToken token);

  private HttpListener? _listener;

  public class EndpointDefinition
  {
    public readonly string Endpoint;
    public readonly AsyncEndpointHandler? HttpCallback;
    public readonly AsyncWebsocketEndpointHandler? WsCallback;
    public readonly HashSet<HttpMethod> Methods;
    public readonly bool SkipDocs;
    public readonly Action<OpenApiOperation> DocsCreator;
    public readonly bool RunOnThreadPool;

    public bool IsWebsocket => WsCallback != null;

    public EndpointDefinition(string endpoint,
                              AsyncEndpointHandler? httpCallback,
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
  internal WebServerLoggerConfig LoggerConfig { get; }

  private readonly ILogger _logger;

  internal readonly List<(Router.RouteDescriptor, EndpointDefinition)> Endpoints = new();
  internal HashSet<IPAddress>? TrustedProxies;

  public int WebsocketSenderQueueLength = 10;

  private CancellationTokenSource? _serverShutdownTokenSource;
  internal CancellationToken ServerShutdownToken => _serverShutdownTokenSource!.Token;

  private OpenApiDocument _openApiDocument = new() {
      SchemaType = SchemaType.OpenApi3,
  };

  public WebServer(LogFactory? factory, WebServerLoggerConfig? loggerConfig = null, SynchronizationContext? synchronizationContext = null)
  {
    LogFactory = factory;
    SynchronizationContext = synchronizationContext;
    LoggerConfig = loggerConfig ?? new WebServerLoggerConfig();
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
    if (!(endpoint.StartsWith("/") || endpoint == "*"))
      throw new ArgumentException("endpoint path must start with slash or be a catch-all one (*)");

    var route = Router.CompileRoute(endpoint);
    Endpoints.Add((route, new EndpointDefinition(endpoint, callback, null, methods, skipDocs, docsCreator, runOnThreadPool)));
  }

  public void AddWsEndpoint(string endpoint, AsyncWebsocketEndpointHandler callback, bool skipDocs = false, Action<OpenApiOperation>? docsCreator = null, bool runOnThreadPool = false)
  {
    if (!(endpoint.StartsWith("/") || endpoint == "*"))
      throw new ArgumentException("endpoint path must start with slash or be a catch-all one (*)");

    var route = Router.CompileRoute(endpoint);
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

    AddGETEndpoint(endpoint, _ => Response.Html(SwaggerHelpers.GenerateSwaggerHtml(endpoint + "/openapi.json", name: Name)));
    AddGETEndpoint(endpoint + "/openapi.json", _ => Response.Text(schemaJson));
  }

  public void RunSync(string host, int port, WebServerSslConfig? sslConfig = null)
  {
    Start(host, port, sslConfig);
    // ReSharper disable once MethodSupportsCancellation
    Task.Run(InternalRun).Wait(ServerShutdownToken);
  }

  public void RunAsync(string host, int port, WebServerSslConfig? sslConfig = null)
  {
    Start(host, port, sslConfig);
    Task.Run(InternalRun, ServerShutdownToken);
  }

  private void Start(string host, int port, WebServerSslConfig? sslConfig)
  {
    _listener = new HttpListener();

    if (sslConfig == null) {
      _logger.Info($"Starting HTTP server on http://{host}:{port}");
      _listener.Prefixes.Add($"http://*:{port}/");
    }
    else {
      throw new ArgumentException("ssl not supported");
    }

    _listener.Start();
    _serverShutdownTokenSource = new();
  }

  public void Stop()
  {
    _listener?.Stop();
    _serverShutdownTokenSource?.Cancel();
  }

  [SuppressMessage("ReSharper", "FunctionNeverReturns")]
  private async Task InternalRun()
  {
    while (!_serverShutdownTokenSource!.IsCancellationRequested) {
      HttpListenerContext? httpContext = null;
      try {
        httpContext = await _listener!.GetContextAsync();
        var connectionTime = DateTime.UtcNow;
        var requestTimer = Stopwatch.StartNew();

        // C# HTTP server automatically sends back /Length Required/ error response
        if (httpContext.Response.StatusCode == 411) {
          continue;
        }

        // workaround: access to httpContext.Request sometimes gives NullReferenceException for some reason
        try {
          _ = httpContext.Request;
        }
        catch (NullReferenceException) {
          try { httpContext.Response.Close(); }
          catch { // ignored
          }

          continue;
        }

        var rq = new RequestDispatcher(this);
        rq.DispatchRequest(httpContext, connectionTime, requestTimer);
      }
      catch (ObjectDisposedException) {
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
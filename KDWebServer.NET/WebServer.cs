using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading.Tasks;
using NLog;
using HttpListener = WebSocketSharp.Net.HttpListener;
using HttpListenerContext = WebSocketSharp.Net.HttpListenerContext;

namespace KDWebServer
{
  public class WebServerSslConfig
  {
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.Tls12;
    public string CertificatePath { get; set; }
    public string KeyPath { get; set; }
    public bool ClientCertificateRequired { get; set; } = false;
    public RemoteCertificateValidationCallback ClientCertificateValidationCallback { get; set; } = (sender, certificate, chain, sslPolicyErrors) => true;
  }

  public class WebServer
  {
    public delegate Task<IWebServerResponse> AsyncEndpointHandler(WebServerRequestContext ctx);

    public delegate IWebServerResponse EndpointHandler(WebServerRequestContext ctx);

    private HttpListener _listener;

    internal class EndpointDefinition
    {
      public readonly string Endpoint;
      public readonly AsyncEndpointHandler Callback;

      public EndpointDefinition(string endpoint, AsyncEndpointHandler callback)
      {
        Endpoint = endpoint;
        Callback = callback;
      }
    }

    internal NLog.LogFactory LogFactory { get; }

    private readonly NLog.ILogger _logger;

    internal readonly Dictionary<Router.RouteDescriptor, EndpointDefinition> Endpoints = new Dictionary<Router.RouteDescriptor, EndpointDefinition>();
    internal HashSet<IPAddress> TrustedProxies;

    public WebServer(NLog.LogFactory factory)
    {
      LogFactory = factory;
      _logger = factory?.GetLogger("webserver") ?? LogManager.LogFactory.CreateNullLogger();
    }

    public void SetTrustedProxies(IEnumerable<IPAddress> trustedProxies)
    {
      TrustedProxies = trustedProxies.ToHashSet();
    }

    public void AddEndpoint(string endpoint, EndpointHandler callback, HashSet<HttpMethod> methods) => AddEndpoint(endpoint, ctx => Task.FromResult(callback(ctx)), methods);

    public void AddEndpoint(string endpoint, AsyncEndpointHandler callback, HashSet<HttpMethod> methods)
    {
      if (!(endpoint.StartsWith("/") || endpoint == "*"))
        throw new ArgumentException("Endpoint path must start with slash or be a catch-all one (*)");

      var route = Router.CompileRoute(endpoint);
      route.Methods = methods;
      Endpoints.Add(route, new EndpointDefinition(endpoint, callback));
    }

    public void AddGETEndpoint(string endpoint, EndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Get });
    public void AddGETEndpoint(string endpoint, AsyncEndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Get });
    public void AddPOSTEndpoint(string endpoint, EndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Post });
    public void AddPOSTEndpoint(string endpoint, AsyncEndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Post });

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
        _listener.Prefixes.Add($"http://{host}:{port}/");
      }
      else {
        _logger.Info($"Starting HTTPS server on {host}:{port}");
        _listener.Prefixes.Add($"https://{host}:{port}/");

        _listener.SslConfiguration.EnabledSslProtocols = sslConfig.EnabledSslProtocols;
        _listener.SslConfiguration.ServerCertificate = Utils.LoadPemCertificate(sslConfig.CertificatePath, sslConfig.KeyPath);
        _listener.SslConfiguration.ClientCertificateRequired = sslConfig.ClientCertificateRequired;
        _listener.SslConfiguration.CheckCertificateRevocation = false;
        _listener.SslConfiguration.ClientCertificateValidationCallback = sslConfig.ClientCertificateValidationCallback;
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

          var handler = new WebServerClientHandler(this, httpContext);
          handler.Handle();
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
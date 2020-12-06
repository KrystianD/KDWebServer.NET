using System;
using System.Collections.Generic;
using System.Diagnostics;
using WebSocketSharp.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading.Tasks;

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
    public delegate Task<IWebServerResponse> EndpointHandler(WebServerRequestContext ctx);

    private HttpListener _listener;

    internal class EndpointDefinition
    {
      public readonly string Endpoint;
      public readonly EndpointHandler Callback;

      public EndpointDefinition(string endpoint, EndpointHandler callback)
      {
        Endpoint = endpoint;
        Callback = callback;
      }
    }

    internal NLog.LogFactory LogFactory { get; }

    private readonly NLog.Logger _logger;

    internal readonly Dictionary<WebServerUtils.RouteDescriptor, EndpointDefinition> Endpoints = new Dictionary<WebServerUtils.RouteDescriptor, EndpointDefinition>();

    public WebServer(NLog.LogFactory factory)
    {
      LogFactory = factory;
      _logger = factory.GetLogger<NLog.Logger>("webserver");
    }

    public void AddEndpoint(string endpoint, EndpointHandler callback, HashSet<HttpMethod> methods)
    {
      if (!(endpoint.StartsWith("/") || endpoint == "*"))
        throw new ArgumentException("Endpoint path must start with slash or be a catch-all one (*)");

      var route = WebServerUtils.CompileRoute(endpoint);
      route.Methods = methods;
      Endpoints.Add(route, new EndpointDefinition(endpoint, callback));
    }

    public void AddGETEndpoint(string endpoint, EndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Get });
    public void AddPOSTEndpoint(string endpoint, EndpointHandler callback) => AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Post });

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
      if (Endpoints.Count == 0)
        return;

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

    private async Task InternalRun()
    {
      while (true) {
        try {
          var httpContext = await Task.Factory.FromAsync(_listener.BeginGetContext, _listener.EndGetContext, null);

          var handler = new WebServerClientHandler(this, httpContext);
          handler.Handle();
        }
        catch (Exception e) {
          _logger.Error(e, "An error occurred during handling webserver client");
        }
      }
    }
  }
}
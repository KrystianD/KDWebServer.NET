using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace KDWebServer
{
  public class WebServer
  {
    public delegate Task<IWebServerResponse> EndpointHandler(WebServerRequestContext ctx);

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

    public async Task Run(int port)
    {
      if (Endpoints.Count == 0)
        return;

      var listener = new HttpListener();
      _logger.Info($"Starting server on port {port}");
      listener.Prefixes.Add($"http://+:{port}/");

      listener.Start();

      while (true) {
        try {
          var httpContext = await listener.GetContextAsync();

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
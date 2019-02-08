using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NLog.Config;

namespace KDWebServer
{
  public class InternalWebServer
  {
    public delegate IWebServerResponse FastEndpointHandler(WebServerRequestContext ctx);

    public delegate Task<IWebServerResponse> EndpointHandler(WebServerRequestContext ctx);

    public class EndpointDefinition
    {
      public readonly string Endpoint;
      public readonly EndpointHandler Callback;

      public EndpointDefinition(string endpoint, EndpointHandler callback)
      {
        Endpoint = endpoint;
        Callback = callback;
      }
    }
    
    public NLog.LogFactory LogFactory { get; }
    public NLog.Logger Logger { get; }

    public readonly Dictionary<WebServerUtils.RouteDescriptor, EndpointDefinition> _endpoints = new Dictionary<WebServerUtils.RouteDescriptor, EndpointDefinition>();

    public InternalWebServer(NLog.LogFactory factory)
    {
      LogFactory = factory;
      Logger = factory.GetLogger<NLog.Logger>("webserver"); 
    }

    public void AddEndpoint(string endpoint, EndpointHandler callback, HashSet<HttpMethod> methods)
    {
      Debug.Assert(endpoint.StartsWith("/") || endpoint == "*");

      var route = WebServerUtils.CompileRoute(endpoint);
      route.Methods = methods;
      _endpoints.Add(route, new EndpointDefinition(endpoint, callback));
    }

    public void AddGETEndpoint(string endpoint, EndpointHandler callback)
    {
      AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Get });
    }

    public void AddPOSTEndpoint(string endpoint, EndpointHandler callback)
    {
      AddEndpoint(endpoint, callback, new HashSet<HttpMethod>() { HttpMethod.Post });
    }

    public async void Run(int port)
    {
      if (_endpoints.Count == 0)
        return;

      var listener = new HttpListener();
      Logger.Info($"starting server on port {port}");
      listener.Prefixes.Add($"http://+:{port}/");

      listener.Start();

      while (true) {
        try {
          var httpContext = await listener.GetContextAsync();

          var handler = new InternalWebServerClientHandler(this, httpContext);
          handler.Handle();
        }
        catch (Exception e) {
          Console.WriteLine(e);
        }
      }
    }
  }
}
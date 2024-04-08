using System;
using System.Net;
using KDWebServer.Handlers;

namespace KDWebServer;

public interface IRequestObserver
{
  void OnNewRequest(HttpListenerContext httpContext);
  void OnRequestMatch(HttpListenerContext httpContext, RequestDispatcher.RouteEndpointMatch match);
  void AfterRequestCallback(HttpListenerContext httpContext, RequestDispatcher.RouteEndpointMatch match, WebServerResponse response, TimeSpan handlerTime, TimeSpan processingTime);
  void AfterRequestSent(HttpListenerContext httpContext, RequestDispatcher.RouteEndpointMatch match, WebServerResponse response, TimeSpan processingTime);
}
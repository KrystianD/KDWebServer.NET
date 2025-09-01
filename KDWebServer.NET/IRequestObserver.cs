using System;
using System.Net;
using KDWebServer.Handlers;
using Microsoft.AspNetCore.Http;

namespace KDWebServer;

public interface IRequestObserver
{
  void OnNewRequest(HttpContext httpContext);
  void OnRequestMatch(HttpContext httpContext, RequestDispatcher.RouteEndpointMatch match);
  void AfterRequestCallback(HttpContext httpContext, RequestDispatcher.RouteEndpointMatch match, WebServerResponse response, TimeSpan handlerTime, TimeSpan processingTime);
  void AfterRequestSent(HttpContext httpContext, RequestDispatcher.RouteEndpointMatch match, WebServerResponse response, TimeSpan processingTime);
}
using System;
using System.Collections.Generic;
using System.Net.Http;
using JetBrains.Annotations;
using KDWebServer.ClassHandler.Attributes;

namespace KDWebServer.ClassHandler.Creator;

public class EndpointDefinition
{
  public record ParameterDefinition(string Name, Type Type, bool IsNullable, MethodParameterBuilder ParameterBuilder);

  public readonly string Path;
  public readonly HttpMethod HttpMethod;

  public Type ReturnType = typeof(object);
  public string Description = "";
  public string Category = "";
  public string ReturnDescription = "";
  public bool IsDeprecated = false;
  public ResponseTypeEnum ResponseType = ResponseTypeEnum.Json;
  public bool RunOnThreadPool = false;
  public readonly List<ParameterDefinition> Parameters = new();

  private EndpointDefinition(string path, HttpMethod httpMethod)
  {
    Path = path;
    HttpMethod = httpMethod;
  }

  public static EndpointBuilder Create(string path, HttpMethod method) => new(new EndpointDefinition(path, method));
}

[PublicAPI]
public class EndpointBuilder
{
  private EndpointDefinition Endpoint { get; }

  internal EndpointBuilder(EndpointDefinition endpoint)
  {
    Endpoint = endpoint;
  }

  public EndpointBuilder AddParameter(string name, Type type, bool isNullable, Action<MethodParameterBuilder>? parameterBuilder = null)
  {
    var pb = new MethodParameterBuilder();
    parameterBuilder?.Invoke(pb);
    Endpoint.Parameters.Add(new EndpointDefinition.ParameterDefinition(name, type, isNullable, pb));
    return this;
  }

  public EndpointBuilder WithReturnType(Type returnType)
  {
    Endpoint.ReturnType = returnType;
    return this;
  }

  public EndpointBuilder WithDescription(string description)
  {
    Endpoint.Description = description;
    return this;
  }

  public EndpointBuilder WithCategory(string category)
  {
    Endpoint.Category = category;
    return this;
  }

  public EndpointBuilder WithReturnDescription(string returnDescription)
  {
    Endpoint.ReturnDescription = returnDescription;
    return this;
  }

  public EndpointBuilder WithDeprecated(bool deprecated = true)
  {
    Endpoint.IsDeprecated = deprecated;
    return this;
  }

  public EndpointBuilder WithResponseType(ResponseTypeEnum responseType)
  {
    Endpoint.ResponseType = responseType;
    return this;
  }

  public EndpointBuilder WithRunOnThreadPool(bool runOnThreadPool = true)
  {
    Endpoint.RunOnThreadPool = runOnThreadPool;
    return this;
  }

  public EndpointDefinition Build() => Endpoint;
}

[PublicAPI]
public class MethodParameterBuilder
{
  internal string Description = "";
  internal DefaultValue DefaultValue = new(false, null);
  internal List<object?> DropdownItems = new();

  public MethodParameterBuilder WithDescription(string description)
  {
    Description = description;
    return this;
  }

  public MethodParameterBuilder WithDefaultValue(object defaultValue)
  {
    DefaultValue = new(true, defaultValue);
    return this;
  }

  public MethodParameterBuilder AddDropdownItem(object? value)
  {
    DropdownItems.Add(value);
    return this;
  }

  public MethodParameterBuilder AddDropdownItems(IEnumerable<object?> values)
  {
    DropdownItems.AddRange(values);
    return this;
  }
}
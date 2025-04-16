namespace KDWebServer.ClassHandler.Creator;

internal struct DefaultValue
{
  public readonly bool HasDefaultValue;
  public readonly bool OnlyForSwagger;
  public readonly object? Value;

  public DefaultValue(bool hasDefaultValue,bool onlyForSwagger, object? value)
  {
    HasDefaultValue = hasDefaultValue;
    OnlyForSwagger = onlyForSwagger;
    Value = value;
  }
}
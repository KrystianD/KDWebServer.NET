namespace KDWebServer.ClassHandler.Creator;

internal struct DefaultValue
{
  public readonly bool HasDefaultValue;
  public readonly object? Value;

  public DefaultValue(bool hasDefaultValue, object? value)
  {
    HasDefaultValue = hasDefaultValue;
    Value = value;
  }
}
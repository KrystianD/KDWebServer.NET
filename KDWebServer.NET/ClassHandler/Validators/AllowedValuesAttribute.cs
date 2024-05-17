using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace KDWebServer.ClassHandler.Validators;

public class AllowedValuesAttribute : ValidationAttribute
{
  public object[] AllowedValues { get; }

  public AllowedValuesAttribute(params object[] allowedValues)
  {
    AllowedValues = allowedValues;
  }

  protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
  {
    if (value == null)
      return new ValidationResult("Null value");

    return AllowedValues.Contains(value) == true
        ? ValidationResult.Success
        : new ValidationResult("Invalid value");
  }
}
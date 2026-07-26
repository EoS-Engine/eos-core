namespace EOS.Contracts;

public interface IProtectionClient
{
    ValidationResult Validate(ActionRequest action);
}

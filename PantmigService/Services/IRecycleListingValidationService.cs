using PantmigService.Entities;

namespace PantmigService.Services;

public record ValidationProblem(string Title, string Detail, int StatusCode);

public class ValidationResult<T>
{
    public bool IsValid => Problem is null;
    public ValidationProblem? Problem { get; init; }
    public T? Value { get; init; }

    public static ValidationResult<T> Failure(string title, string detail, int statusCode) => new()
    {
        Problem = new ValidationProblem(title, detail, statusCode)
    };

    public static ValidationResult<T> Success(T value) => new() { Value = value };
}

public record CreateListingItemInput(RecycleMaterialType Type, int Quantity, string? DepositClass, decimal? EstimatedDepositPerUnit);

public record CreateListingValidated(
    string Title,
    string Description,
    string CityInput,
    DateTime AvailableFrom,
    DateTime AvailableTo,
    List<CreateListingItemInput> Items,
    decimal? EstimatedValue);

public interface IRecycleListingValidationService
{
    ValidationResult<CreateListingValidated> ValidateCreate(
        string? title,
        string? description,
        string? city,
        string? location,
        DateTime availableFrom,
        DateTime availableTo,
        List<CreateListingItemInput>? items);

    ValidationResult<object> ValidateReceiptUpload(IFormFile? file);

    bool IsImage(string? contentType);
}

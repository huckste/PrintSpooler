using ErrorOr;

namespace PrintSpooler.Api.Extensions;

public static class ErrorOrExtensions
{
  public static int? ToStatusCode(this List<Error> errors)
  {
    var statusCode = errors.First().Type switch
    {
      ErrorType.NotFound => StatusCodes.Status404NotFound,
      ErrorType.Conflict => StatusCodes.Status409Conflict,
      ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
      ErrorType.Forbidden => StatusCodes.Status403Forbidden,
      ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
      ErrorType.Failure => StatusCodes.Status400BadRequest,
      ErrorType.Unexpected => StatusCodes.Status500InternalServerError,
      _ => StatusCodes.Status500InternalServerError
    };

    return statusCode;
  }
}

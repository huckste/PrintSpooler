namespace PrintSpooler.Web.Services;

using ErrorOr;
using Microsoft.AspNetCore.WebUtilities;

public class ApiClient(IHttpClientFactory httpClientFactory)
{
  private readonly HttpClient client = httpClientFactory.CreateClient("PrintSpoolerApi");

  private static async Task<ErrorOr<T>> Safe<T>(Func<Task<ErrorOr<T>>> action)
  {
    try
    {
      return await action();
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      return Error.Failure("Api.RequestFailed", ex.Message);
    }
  }

  public Task<ErrorOr<List<T>>> Get<T>(string url) =>
      Safe<List<T>>(async () => await client.GetFromJsonAsync<List<T>>(url) ?? []);

  public Task<ErrorOr<T?>> Get<T>(string url, Guid id) =>
      Safe<T?>(async () => await client.GetFromJsonAsync<T>($"{url}/{id}"));

  public Task<ErrorOr<TResponse>> Get<TResponse, TQuery>(string url, TQuery queryParams) =>
      Safe<TResponse>(async () =>
      {
        var baseAddress = client.BaseAddress;

        if (baseAddress is null)
          return Error.Failure("Api.BaseAdress", "Base address cannot be null");

        var parameters = new RouteValueDictionary(queryParams).ToDictionary(
            k => k.Key,
            v => v.Value?.ToString()
        );

        var requestUri = QueryHelpers.AddQueryString(url, parameters);
        Console.WriteLine(requestUri);

        var result = await client.GetFromJsonAsync<TResponse>(requestUri);

        return result is null
            ? Error.Failure("Api.EmptyResponse", "Server returned no content")
            : result;
      });

  public Task<ErrorOr<HttpResponseMessage>> HealthCheck() =>
      Safe<HttpResponseMessage>(async () => await client.GetAsync("/health"));

  public Task<ErrorOr<TResponse>> Post<TResponse>(string url, object value) =>
      Safe<TResponse>(async () =>
      {
        var response = await client.PostAsJsonAsync(url, value);

        if (!response.IsSuccessStatusCode)
        {
          var body = await response.Content.ReadAsStringAsync();
          return Error.Failure($"{(int)response.StatusCode}", body);
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>();

        return result is null
            ? Error.Failure("Api.EmptyResponse", "Server returned no content")
            : result;
      });

  public Task<ErrorOr<Success>> Delete(string url) =>
      Safe<Success>(async () =>
      {
        var response = await client.DeleteAsync(url);

        if (response.IsSuccessStatusCode)
          return Result.Success;

        var body = await response.Content.ReadAsStringAsync();
        return Error.Failure($"{(int)response.StatusCode}", body);
      });
}
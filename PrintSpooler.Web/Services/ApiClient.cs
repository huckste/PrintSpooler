namespace PrintSpooler.Web.Services;

using ErrorOr;
using Microsoft.AspNetCore.WebUtilities;

public class ApiClient(IHttpClientFactory httpClientFactory)
{
  private readonly HttpClient client = httpClientFactory.CreateClient("PrintSpoolerApi");

  public async Task<ErrorOr<List<T>>> Get<T>(string url)
  {
    try
    {
      return await client.GetFromJsonAsync<List<T>>(url) ?? [];
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      return Error.Failure("Api.RequestFailed", ex.Message);
    }
  }

  public async Task<ErrorOr<T?>> Get<T>(string url, Guid id)
  {
    try
    {
      return await client.GetFromJsonAsync<T>($"{url}/{id}");
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      return Error.Failure("Api.RequestFailed", ex.Message);
    }
  }

  public async Task<ErrorOr<TResponse>> Get<TResponse, TQuery>(string url, TQuery queryParams)
  {
    try
    {
      var baseAddress = client.BaseAddress;

      if (baseAddress is null)
        return Error.Failure("Api.BaseAdress", "Base address cannot be null");

      var builder = new UriBuilder(baseAddress);

      var parameters = new RouteValueDictionary(queryParams).ToDictionary(
          k => k.Key,
          v => v.Value?.ToString()
      );

      var requestUri = QueryHelpers.AddQueryString(url, parameters);
      Console.WriteLine(requestUri);

      var result = await client.GetFromJsonAsync<TResponse>(requestUri);

      if (result is null)
        return Error.Failure("Api.EmptyResponse", "Server returned no content");

      return result;
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      return Error.Failure("Api.RequestFailed", ex.Message);
    }
  }

  public async Task<ErrorOr<HttpResponseMessage>> HealthCheck()
  {
    try
    {
      return await client.GetAsync("/health");
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      return Error.Failure("Api.RequestFailed", ex.Message);
    }
  }

  public async Task<ErrorOr<TResponse>> Post<TResponse>(string url, object value)
  {
    try
    {
      var response = await client.PostAsJsonAsync(url, value);

      if (response.IsSuccessStatusCode)
      {
        var result = await response.Content.ReadFromJsonAsync<TResponse>();

        if (result is null)
          return Error.Failure("Api.EmptyResponse", "Server returned no content");

        return result;
      }

      var body = await response.Content.ReadAsStringAsync();
      return Error.Failure($"{(int)response.StatusCode}", body);
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      return Error.Failure("refused", ex.Message);
    }
  }

  public async Task<ErrorOr<Success>> Delete(string url)
  {
    try
    {
      var response = await client.DeleteAsync(url);

      if (response.IsSuccessStatusCode)
        return Result.Success;

      var body = await response.Content.ReadAsStringAsync();
      return Error.Failure($"{(int)response.StatusCode}", body);
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      return Error.Failure("refused", ex.Message);
    }
  }
}

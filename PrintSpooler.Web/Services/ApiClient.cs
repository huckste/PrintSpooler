namespace PrintSpooler.Web.Services;

using ErrorOr;

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
                return (await response.Content.ReadFromJsonAsync<TResponse>())!;

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

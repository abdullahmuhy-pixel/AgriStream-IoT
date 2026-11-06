using System.Net.Http.Json;
using System.Text.Json;
using AgriStream.Web.Models;

namespace AgriStream.Web.Services;

/// <summary>
/// Calls the HTTP-triggered Azure Functions. The function key is sent in the x-functions-key header.
/// </summary>
public class FunctionApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string? _functionKey;

    public FunctionApiClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _functionKey = config["Functions:Key"];
    }

    /// <summary>HTTP GET function: latest telemetry for a field from Azure Table Storage.</summary>
    public async Task<List<TelemetryReading>> GetLatestTelemetryAsync(string fieldId, int take, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"telemetry/{Uri.EscapeDataString(fieldId)}?take={take}");
        AddKey(request);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TelemetryReading>>(JsonOptions, ct) ?? new();
    }

    /// <summary>HTTP POST function: places an irrigation command on the Azure Storage Queue.</summary>
    public async Task<string> SendIrrigationCommandAsync(IrrigationRequest payload, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "irrigation")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        AddKey(request);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Irrigation function returned {(int)response.StatusCode}: {body}");
        return body;
    }

    private void AddKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_functionKey))
            request.Headers.Add("x-functions-key", _functionKey);
    }
}

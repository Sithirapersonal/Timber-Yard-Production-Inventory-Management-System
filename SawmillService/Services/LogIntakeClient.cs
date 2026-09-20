using System.Net.Http.Headers;
using System.Text.Json;
using SawmillService.DTOs;

namespace SawmillService.Services;

/// <summary>
/// HTTP client that calls LogIntakeService on behalf of the current user,
/// forwarding their own Bearer token on every request.
/// Registered via AddHttpClient&lt;LogIntakeClient&gt; in Program.cs.
/// Base address comes from Services:LogIntakeApiUrl config key.
/// </summary>
public class LogIntakeClient
{
    private readonly HttpClient _http;
    private readonly ILogger<LogIntakeClient> _logger;

    // JSON options matching ASP.NET Core's default camelCase serialization
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LogIntakeClient(HttpClient http, ILogger<LogIntakeClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read: stock overview
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls GET /stock on LogIntakeService and returns the stock list.
    /// Throws <see cref="LogIntakeServiceException"/> on any non-success response.
    /// </summary>
    public async Task<List<RawStockOverviewDto>> GetStockAsync(string bearerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "stock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network error contacting LogIntakeService /stock");
            throw new LogIntakeServiceException("Could not reach LogIntakeService. Check that it is running.");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LogIntakeService /stock returned {StatusCode}", response.StatusCode);
            throw new LogIntakeServiceException(
                $"LogIntakeService returned {(int)response.StatusCode} when fetching stock.");
        }

        var items = await response.Content.ReadFromJsonAsync<List<RawStockOverviewDto>>(_jsonOpts);
        return items ?? new List<RawStockOverviewDto>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read: in-stock logs for a given species/length combination
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls GET /logs?speciesId=&amp;lengthId= on LogIntakeService and returns InStock logs.
    /// Throws <see cref="LogIntakeServiceException"/> on any non-success response.
    /// </summary>
    public async Task<List<LogItemDto>> GetLogsAsync(string bearerToken, int? speciesId, int? lengthId)
    {
        var qs = new List<string>();
        if (speciesId.HasValue) qs.Add($"speciesId={speciesId.Value}");
        if (lengthId.HasValue) qs.Add($"lengthId={lengthId.Value}");
        var queryString = qs.Count > 0 ? "?" + string.Join("&", qs) : string.Empty;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"logs{queryString}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network error contacting LogIntakeService /logs");
            throw new LogIntakeServiceException("Could not reach LogIntakeService. Check that it is running.");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LogIntakeService /logs returned {StatusCode}", response.StatusCode);
            throw new LogIntakeServiceException(
                $"LogIntakeService returned {(int)response.StatusCode} when fetching logs.");
        }

        var logs = await response.Content.ReadFromJsonAsync<List<LogItemDto>>(_jsonOpts);
        return logs ?? new List<LogItemDto>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Write: mark logs as Consumed
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls PUT /logs/consume on LogIntakeService to mark logs as Consumed.
    /// Returns true on 200 OK; false if LogIntakeService returns 400/404 (logs not eligible).
    /// Throws <see cref="LogIntakeServiceException"/> on network error or unexpected status.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> ConsumeLogsAsync(
        string bearerToken,
        IEnumerable<int> logIds)
    {
        var payload = new ConsumeLogsRequestDto { LogIds = logIds.ToList() };

        using var request = new HttpRequestMessage(HttpMethod.Put, "logs/consume");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = JsonContent.Create(payload);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network error contacting LogIntakeService /logs/consume");
            throw new LogIntakeServiceException("Could not reach LogIntakeService to mark logs as Consumed.");
        }

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("LogIntakeService /logs/consume returned {Status}: {Body}", response.StatusCode, body);
            return (false, $"LogIntakeService refused to mark logs as Consumed: {response.StatusCode}");
        }

        // Unexpected status — treat as upstream failure
        throw new LogIntakeServiceException(
            $"LogIntakeService returned unexpected status {(int)response.StatusCode} for consume-logs.");
    }
}

/// <summary>Thrown when LogIntakeService is unreachable or returns an upstream error.</summary>
public class LogIntakeServiceException : Exception
{
    public LogIntakeServiceException(string message) : base(message) { }
}

/// <summary>
/// Projection of LogIntakeService's LogItem that SawmillService needs.
/// Must match the JSON field names returned by LogIntakeController GET /logs.
/// </summary>
public class LogItemDto
{
    public int LogId { get; set; }
    public int StockId { get; set; }
    public int SpeciesId { get; set; }
    public string SpeciesName { get; set; } = string.Empty;
    public int LengthId { get; set; }
    public decimal LengthFt { get; set; }
    public decimal GirthFt { get; set; }
    public decimal VolumeM3 { get; set; }
    public string Status { get; set; } = "InStock";
    public string? SupplierName { get; set; }
    public DateTime CreatedAt { get; set; }
}

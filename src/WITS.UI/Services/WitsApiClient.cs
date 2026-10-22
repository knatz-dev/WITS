using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WITS.Common;
using WITS.Models;

namespace WITS.UI.Services;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string? Token, string? Message);

public class WitsApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>Talks to the WITS API and attaches the current circuit's bearer token.</summary>
public class WitsApiClient(HttpClient http, AuthSession session)
{
    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "api/v1/auth/login", new LoginRequest(username, password), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        LoginResponse? login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
        return login?.Token is null
            ? throw new WitsApiException("The API did not return an access token.")
            : login;
    }

    public async Task<PagedResult<Ticket>> GetTicketsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(
            $"api/v1/ticket?page={page}&pageSize={pageSize}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<PagedResult<Ticket>>(cancellationToken)
            ?? new PagedResult<Ticket>([], 0, page, pageSize);
    }

    public async Task<Ticket?> GetTicketAsync(long id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync($"api/v1/ticket/{id}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<Ticket>(cancellationToken);
    }

    private Task<HttpResponseMessage> SendAsync(string uri, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        if (session.Token is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http.SendAsync(request, cancellationToken);
    }

    private static async Task<WitsApiException> CreateExceptionAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ProblemResponse? problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            string message = problem?.Detail
                ?? problem?.Title
                ?? $"The API returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
            if (!string.IsNullOrWhiteSpace(problem?.TraceId))
            {
                message += $" (traceId: {problem.TraceId})";
            }
            return new WitsApiException(message, response.StatusCode);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new WitsApiException(
                $"The API returned {(int)response.StatusCode} ({response.ReasonPhrase}).", response.StatusCode);
        }
    }

    private sealed record ProblemResponse(string? Title, string? Detail, string? TraceId);
}

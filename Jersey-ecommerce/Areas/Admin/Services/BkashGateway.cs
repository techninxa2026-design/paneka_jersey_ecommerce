using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Jersey_ecommerce.Services.Checkout;

public sealed class BkashSettings
{
    public string BaseUrl { get; set; } = "https://tokenized.sandbox.bka.sh/v2/tokenized-checkout";
    public string AppKey { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string CallbackUrl { get; set; } = "";
}

public sealed record BkashCreated(string PaymentId, string RedirectUrl);
public sealed record BkashResult(
    string PaymentId, string TransactionId, string Status,
    decimal Amount, string Currency, string MerchantInvoice, bool Valid,
    string? Message = null);

public sealed class BkashGateway
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly BkashSettings _s;
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private const string TokenCacheKey = "PANENKA_BKASH_V2_TOKEN";

    public BkashGateway(HttpClient http, IMemoryCache cache, IOptions<BkashSettings> options)
    {
        _http = http;
        _cache = cache;
        _s = options.Value;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public string CallbackUrl => _s.CallbackUrl;
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_s.AppKey) &&
        !string.IsNullOrWhiteSpace(_s.AppSecret) &&
        !string.IsNullOrWhiteSpace(_s.Username) &&
        !string.IsNullOrWhiteSpace(_s.Password) &&
        Uri.TryCreate(_s.CallbackUrl, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        !u.IsLoopback &&
        !u.Host.Equals("YOUR-DOMAIN.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsTrustedCheckoutUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            return false;
        return u.Host.Equals("bkash.com", StringComparison.OrdinalIgnoreCase)
            || u.Host.EndsWith(".bkash.com", StringComparison.OrdinalIgnoreCase)
            || u.Host.Equals("bka.sh", StringComparison.OrdinalIgnoreCase)
            || u.Host.EndsWith(".bka.sh", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<BkashCreated> CreateAsync(decimal amount, string invoice,
        string payerReference, string callbackUrl, CancellationToken ct = default)
    {
        if (amount <= 0) throw new InvalidOperationException("Order amount must be positive.");
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var callback)
            || callback.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Set a public HTTPS Bkash:CallbackUrl.");

        using var doc = await SendAsync("payment/create", new
        {
            mode = "0011",
            payerReference,
            callbackURL = callbackUrl,
            amount = amount.ToString("F2", CultureInfo.InvariantCulture),
            currency = "BDT",
            intent = "sale",
            merchantInvoiceNumber = invoice
        }, ct);
        var root = doc.RootElement;
        var paymentId = Read(root, "paymentId", "paymentID");
        var url = Read(root, "bkashURL", "bKashURL");
        if (string.IsNullOrWhiteSpace(paymentId) || !IsTrustedCheckoutUrl(url))
            throw new InvalidOperationException("Invalid bKash checkout response.");
        return new BkashCreated(paymentId, url);
    }

    public Task<BkashResult> ExecuteAsync(string paymentId, CancellationToken ct = default)
        => GetResultAsync("payment/execute", paymentId, ct);

    public Task<BkashResult> QueryAsync(string paymentId, CancellationToken ct = default)
        => GetResultAsync("payment/status", paymentId, ct);

    private async Task<BkashResult> GetResultAsync(string path, string id, CancellationToken ct)
    {
        using var doc = await SendAsync(path, new { paymentId = id }, ct);
        var root = doc.RootElement;
        var raw = Read(root, "amount");
        var validAmount = decimal.TryParse(raw, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var amount);
        var statusCode = Read(root, "statusCode");
        // bKash v2 may omit statusCode on a successful execute/query response.
        var valid = validAmount && (string.IsNullOrWhiteSpace(statusCode) || statusCode == "0000");
        return new BkashResult(
            Read(root, "paymentId", "paymentID"),
            Read(root, "trxId", "trxID"),
            Read(root, "transactionStatus"),
            amount,
            Read(root, "currency"),
            Read(root, "merchantInvoiceNumber"),
            valid,
            Read(root, "statusMessage"));
    }

    private async Task<JsonDocument> SendAsync(string path, object body, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var request = NewPost(path, body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-app-key", _s.AppKey);
        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"bKash {path} returned HTTP {(int)response.StatusCode}.");
        var doc = JsonDocument.Parse(json);
        var code = Read(doc.RootElement, "statusCode");
        if (!string.IsNullOrEmpty(code) && code != "0000")
        {
            var message = Read(doc.RootElement, "statusMessage");
            doc.Dispose();
            throw new InvalidOperationException($"bKash request rejected: {message} ({code}).");
        }
        return doc;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;
        await TokenLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out cached) && !string.IsNullOrWhiteSpace(cached))
                return cached;
            RequireSettings();
            using var request = NewPost("auth/grant-token", new
            {
                app_key = _s.AppKey,
                app_secret = _s.AppSecret
            });
            request.Headers.Add("username", _s.Username);
            request.Headers.Add("password", _s.Password);
            using var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"bKash auth returned HTTP {(int)response.StatusCode}.");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var code = Read(root, "statusCode");
            if (!string.IsNullOrEmpty(code) && code != "0000")
                throw new InvalidOperationException("bKash authentication rejected the credentials.");
            var token = Read(root, "id_token");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("bKash authentication returned no token.");
            var expires = int.TryParse(Read(root, "expires_in"), out var x) ? x : 3600;
            _cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(30, expires - 90)));
            return token;
        }
        finally { TokenLock.Release(); }
    }

    private HttpRequestMessage NewPost(string path, object body)
    {
        var baseUrl = _s.BaseUrl.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || !(uri.Host == "tokenized.sandbox.bka.sh" || uri.Host == "tokenized.pay.bka.sh")
            || uri.AbsolutePath != "/v2/tokenized-checkout")
            throw new InvalidOperationException("Invalid bKash v2 base URL configuration.");
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/" + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    private void RequireSettings()
    {
        if (new[] { _s.AppKey, _s.AppSecret, _s.Username, _s.Password }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("bKash sandbox/live credentials are not configured.");
    }

    private static string Read(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v))
                return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();
        return "";
    }
}

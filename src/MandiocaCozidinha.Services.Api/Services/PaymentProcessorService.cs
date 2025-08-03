using MandiocaCozidinha.Services.Api.Contracts;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MandiocaCozidinha.Services.Api.Services
{
    public interface IPaymentProcessorService
    {
        Task<HttpStatusCode> SendPaymentAsync(PaymentProcessorRequest request, string clientName = "default", CancellationToken cancellationToken = default);
        Task<PaymentHealthResponse> GetServiceHealth(string clientName = "default", CancellationToken cancellationToken = default);
        Task<bool> SendPaymentWithFailoverAsync(PaymentProcessorRequest request, CancellationToken cancellationToken = default);
        Task<AdminPaymenSummaryRequest> GetAdminPaymentSummary(string clientName = "default", CancellationToken cancellationToken = default);
    }

    public class PaymentProcessorService : IPaymentProcessorService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ILogger<PaymentProcessorService> _logger;

        public PaymentProcessorService(
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache,
            IConnectionMultiplexer connectionMultiplexer,
            ILogger<PaymentProcessorService> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
            _logger = logger;
        }

        public async Task<AdminPaymenSummaryRequest> GetAdminPaymentSummary(string clientName = "default", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(clientName);

            var httpClient = _httpClientFactory.CreateClient(clientName);
            var response = await httpClient.GetAsync("/admin/payments-summary");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AdminPaymenSummaryRequest>() ?? throw new InvalidOperationException("payment-summary error");
        }

        public async Task<PaymentHealthResponse> GetServiceHealth(string clientName = "default", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(clientName);

            var httpClient = _httpClientFactory.CreateClient(clientName);
            var response = await httpClient.GetAsync("/payments/service-health");

            if(response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new PaymentHealthResponse
                {
                    TooManyRequests = true
                };
            }
            else
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<PaymentHealthResponse>() ?? throw new InvalidOperationException("health error");
            }

        }

        public async Task<HttpStatusCode> SendPaymentAsync(PaymentProcessorRequest request, string clientName = "default", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(clientName);

            try
            {
                var httpClient = _httpClientFactory.CreateClient(clientName);
                var j = JsonSerializer.Serialize(request);
                var response = await httpClient.PostAsync("/payments", new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

                if ( response.StatusCode == HttpStatusCode.OK)
                    _memoryCache.Set("semaforo-" + clientName, "verde");

                return response.StatusCode;
            }
            catch (Exception)
            {
                _memoryCache.Set("semaforo-" + clientName, "vermelho");
                return HttpStatusCode.InternalServerError; 
            }
           
        }

        public async Task<bool> SendPaymentWithFailoverAsync(PaymentProcessorRequest request, CancellationToken cancellationToken = default)
        {
            var db = _connectionMultiplexer.GetDatabase();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_memoryCache.TryGetValue("semaforo-default", out string semaforoDefault) && semaforoDefault == "verde")
                {
                    var r = await SendPaymentAsync(request, "default", cancellationToken);
                    if (r == HttpStatusCode.OK)
                    {
                        double score = new DateTimeOffset(request.RequestedAt).ToUnixTimeSeconds();
                        if (await db.SetAddAsync("processed-ids", request.CorrelationId.ToString()))
                            await db.SortedSetAddAsync("payment-requests-default", JsonSerializer.Serialize(request), score);
                        return true;
                    }
                    else if (r == HttpStatusCode.UnprocessableEntity)
                        return true;
                    return false;
                }
                else if (_memoryCache.TryGetValue("semaforo-fallback", out string semaforoFallback) && semaforoFallback == "verde")
                {
                    var r = await SendPaymentAsync(request, "fallback", cancellationToken);
                    if (r == HttpStatusCode.OK)
                    {
                        double score = new DateTimeOffset(request.RequestedAt).ToUnixTimeSeconds();
                        if (await db.SetAddAsync("processed-ids", request.CorrelationId.ToString()))
                            await db.SortedSetAddAsync("payment-requests-fallback", JsonSerializer.Serialize(request), score);
                        return true;
                    } 
                    else if (r == HttpStatusCode.UnprocessableEntity)
                        return true;
                    return false;
                }
                double scoreBackOff = new DateTimeOffset(request.RequestedAt).ToUnixTimeSeconds();
                await db.SortedSetAddAsync("payment-requests-backoff", JsonSerializer.Serialize(request), scoreBackOff);
                return false;
            }
            throw new OperationCanceledException("The operation was canceled before completion.");
        }
    }

    public static class ProcessorPaymentServiceDependencyInjection
    {
        public static void AddProcessorPaymentServices(this IServiceCollection services, IConfiguration configuration)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                MaxConnectionsPerServer = int.MaxValue 
            };

            services.AddHttpClient("default", client =>
            {
                client.BaseAddress = new Uri(configuration["Services:PaymentProcessor:Default"] ?? throw new ArgumentNullException("invalid env Services:PaymentProcessor:Default"));
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("X-Rinha-Token", configuration["Services:PaymentProcessor:Token"] ?? throw new ArgumentNullException("The token for the Payment Processor service is not configured."));
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

            services.AddHttpClient("fallback", client =>
            {
                client.BaseAddress = new Uri(configuration["Services:PaymentProcessor:Fallback"] ?? throw new ArgumentNullException("env invalid Services:PaymentProcessor:Fallback"));
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("X-Rinha-Token", configuration["Services:PaymentProcessor:Token"] ?? throw new ArgumentNullException("env invalid Services:PaymentProcessor:Token"));
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

            services.AddScoped<IPaymentProcessorService, PaymentProcessorService>();
        }
    }
}

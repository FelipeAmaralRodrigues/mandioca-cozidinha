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
        Task<PaymentProcessorResponse> SendPaymentAsync(PaymentProcessorRequest request, string clientName = "default", CancellationToken cancellationToken = default);
        Task<PaymentHealthResponse> GetServiceHealth(string clientName = "default", CancellationToken cancellationToken = default);
        Task<PaymentProcessorResponse> SendPaymentWithFailoverAsync(PaymentProcessorRequest request, CancellationToken cancellationToken = default);
    }

    public class PaymentProcessorService : IPaymentProcessorService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly IConnectionMultiplexer _connectionMultiplexer;

        public PaymentProcessorService(
            IHttpClientFactory httpClientFactory, 
            IMemoryCache memoryCache, 
            IConnectionMultiplexer connectionMultiplexer)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
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

        public async Task<PaymentProcessorResponse> SendPaymentAsync(PaymentProcessorRequest request, string clientName = "default", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(clientName);
            try
            {
                var httpClient = _httpClientFactory.CreateClient(clientName);
                var response = await httpClient.PostAsync("/payments", new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<PaymentProcessorResponse>() ?? throw new InvalidOperationException($"{clientName} client error");
            }
            catch (Exception)
            {
                _memoryCache.Set($"semaforo-{clientName}", "vermelho", TimeSpan.FromSeconds(10));
                throw;
            }
        }

        public async Task<PaymentProcessorResponse> SendPaymentWithFailoverAsync(PaymentProcessorRequest request, CancellationToken cancellationToken = default)
        {
            if (_memoryCache.TryGetValue("semaforo-default", out string semaforoDefault) && semaforoDefault == "verde")
            {
                var response = await SendPaymentAsync(request, "default", cancellationToken);
                var db = _connectionMultiplexer.GetDatabase();
                double score = new DateTimeOffset(request.RequestedAt).ToUnixTimeSeconds();
                await db.SortedSetAddAsync("payment-requests-default", JsonSerializer.Serialize(request), score);
                return response;
            }
            else if (_memoryCache.TryGetValue("semaforo-fallback", out string semaforoFallback) && semaforoFallback == "verde")
            {
                var response = await SendPaymentAsync(request, "fallback", cancellationToken);
                var db = _connectionMultiplexer.GetDatabase();
                double score = new DateTimeOffset(request.RequestedAt).ToUnixTimeSeconds();
                await db.SortedSetAddAsync("payment-requests-fallback", JsonSerializer.Serialize(request), score);
                return response;
            }
            else
            {
                throw new InvalidOperationException("Both payment processors are unavailable at the moment.");
            }
        }
    }

    public static class ProcessorPaymentServiceDependencyInjection
    {
        public static void AddProcessorPaymentServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient("default", client =>
            {
                client.BaseAddress = new Uri(configuration["Services:PaymentProcessor:Default"] ?? throw new ArgumentNullException("invalid env Services:PaymentProcessor:Default"));
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("X-Rinha-Token", configuration["Services:PaymentProcessor:Token"] ?? throw new ArgumentNullException("The token for the Payment Processor service is not configured."));
            });

            services.AddHttpClient("fallback", client =>
            {
                client.BaseAddress = new Uri(configuration["Services:PaymentProcessor:Fallback"] ?? throw new ArgumentNullException("env invalid Services:PaymentProcessor:Fallback"));
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("X-Rinha-Token", configuration["Services:PaymentProcessor:Token"] ?? throw new ArgumentNullException("env invalid Services:PaymentProcessor:Token"));
            });

            services.AddTransient<IPaymentProcessorService, PaymentProcessorService>();
        }
    }
}

using MandiocaCozidinha.Services.Api.Contracts;
using MandiocaCozidinha.Services.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using System.Text.Json;

namespace MandiocaCozidinha.Services.Api.HostedServices
{
    public class PaymentProcessorHealthCheckHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<PaymentProcessorHealthCheckHostedService> _logger;
        private readonly IMemoryCache _memoryCache;

        public PaymentProcessorHealthCheckHostedService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<PaymentProcessorHealthCheckHostedService> logger,
            IMemoryCache memoryCache)
        {
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryCache = memoryCache;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var paymentProcessorService = serviceProvider.GetRequiredService<IPaymentProcessorService>();
                var connectionMultiplexer = serviceProvider.GetRequiredService<IConnectionMultiplexer>();

                while (!cancellationToken.IsCancellationRequested)
                {
                    var db = connectionMultiplexer.GetDatabase();

                    PaymentHealthResponse defaultCheck = null;
                    try
                    {
                        defaultCheck = await paymentProcessorService.GetServiceHealth("default", cancellationToken);
                        var s1 = await db.StringGetAsync("semaforo-default-max-timeout");
                        if (!s1.HasValue || defaultCheck.MinResponseTime > JsonSerializer.Deserialize<PaymentHealthResponse>(s1).MinResponseTime)
                            await db.StringSetAsync("semaforo-default-max-timeout", JsonSerializer.Serialize(defaultCheck));

                        if (!defaultCheck.TooManyRequests)
                            _memoryCache.Set("semaforo-default", defaultCheck.Failing || defaultCheck.MinResponseTime > 2000 ? "vermelho" : "verde", TimeSpan.FromSeconds(5));
                        else
                        {
                            _memoryCache.TryGetValue("semaforo-default", out string sinal);
                            _memoryCache.Set("semaforo-default", sinal, TimeSpan.FromSeconds(10));
                        }
                    }
                    catch (Exception)
                    {
                        _memoryCache.TryGetValue("semaforo-default", out string sinal);
                        _memoryCache.Set("semaforo-default", sinal, TimeSpan.FromSeconds(10));
                    }

                    try
                    {
                        var fallbackCheck = await paymentProcessorService.GetServiceHealth("fallback", cancellationToken);
                        var s2 = await db.StringGetAsync("semaforo-fallback-max-timeout");
                        if (defaultCheck != null && (!s2.HasValue || defaultCheck.MinResponseTime > JsonSerializer.Deserialize<PaymentHealthResponse>(s2).MinResponseTime))
                            await db.StringSetAsync("semaforo-fallback-max-timeout", JsonSerializer.Serialize(defaultCheck));

                        if (!fallbackCheck.TooManyRequests)
                        {
                            _memoryCache.TryGetValue("semaforo-default", out string sinal);
                            _memoryCache.Set("semaforo-fallback", sinal == "vermelho" && (fallbackCheck.Failing || fallbackCheck.MinResponseTime > 500) ? "vermelho" : "verde", TimeSpan.FromSeconds(5));
                        }
                        else
                        {
                            _memoryCache.TryGetValue("semaforo-fallback", out string sinal);
                            _memoryCache.Set("semaforo-fallback", sinal, TimeSpan.FromSeconds(10));
                        }
                    }
                    catch (Exception)
                    {
                        _memoryCache.TryGetValue("semaforo-fallback", out string sinal);
                        _memoryCache.Set("semaforo-fallback", sinal, TimeSpan.FromSeconds(10));
                    }

                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                await StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                await StopAsync(cancellationToken);
            }
        }
    }
}

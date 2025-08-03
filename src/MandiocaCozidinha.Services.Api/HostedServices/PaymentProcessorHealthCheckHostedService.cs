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
                        if (!defaultCheck.TooManyRequests)
                            _memoryCache.Set("semaforo-default", defaultCheck.Failing ? "vermelho" : "verde");
                        await db.SortedSetAddAsync("semaforo-default-healths", JsonSerializer.Serialize(defaultCheck), new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds());
                    }
                    catch (Exception)
                    {
                        _memoryCache.TryGetValue("semaforo-default", out string sinal);
                        _memoryCache.Set("semaforo-default", sinal);
                        await db.SortedSetAddAsync("semaforo-default-healths", JsonSerializer.Serialize(defaultCheck), new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds());
                    }

                    PaymentHealthResponse fallbackCheck = null;
                    try
                    {
                        fallbackCheck = await paymentProcessorService.GetServiceHealth("fallback", cancellationToken);
                        if (!fallbackCheck.TooManyRequests)
                            _memoryCache.Set("semaforo-fallback", fallbackCheck.Failing ? "vermelho" : "verde");
                        await db.SortedSetAddAsync("semaforo-fallback-healths", JsonSerializer.Serialize(fallbackCheck), new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds());
                    }
                    catch (Exception)
                    {
                        await db.SortedSetAddAsync("semaforo-fallback-healths", JsonSerializer.Serialize(fallbackCheck), new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds());
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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

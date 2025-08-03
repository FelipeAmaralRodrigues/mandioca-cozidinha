using MandiocaCozidinha.Services.Api.Configurations;
using MandiocaCozidinha.Services.Api.Contracts;
using MandiocaCozidinha.Services.Api.HostedServices;
using MandiocaCozidinha.Services.Api.Services;
using StackExchange.Redis;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenConfig();
builder.Services.AddProcessorPaymentServices(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConfiguration(builder.Configuration.GetSection("Logging"));
    loggingBuilder.AddConsole();
});

builder.Services.AddHostedService<PaymentProcessorHealthCheckHostedService>();
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>(a =>
{
    var capacity = int.Parse(builder.Configuration["QueueCapacity"]);
    return new BackgroundTaskQueue(capacity);
});
builder.Services.AddHostedService<QueuedHostedService>();

builder.Services.AddSingleton<IConnectionMultiplexer, ConnectionMultiplexer>(a =>
{
    var configuration = builder.Configuration["Redis:Server"];
    if (string.IsNullOrEmpty(configuration))
    {
        throw new InvalidOperationException("Connection string for Redis is not configured.");
    }
    
    return ConnectionMultiplexer.Connect(configuration);
});

var app = builder.Build();
app.UseOpenApiWithScalarConfig();
app.UseHttpsRedirection();

app.MapPost("/payments", async (
    PaymentRequest request, 
    IPaymentProcessorService paymentProcessorService,
    IBackgroundTaskQueue backgroundTaskQueueProvider,
    CancellationToken cancellationToken) =>
{
    await backgroundTaskQueueProvider.QueueBackgroundWorkItemAsync(async token => 
    {
        var failPayment = false;
        var dateTimeNow = DateTime.UtcNow;

        while (!failPayment && !cancellationToken.IsCancellationRequested)
        {
            failPayment = await paymentProcessorService.SendPaymentWithFailoverAsync(new PaymentProcessorRequest
            {
                CorrelationId = request.CorrelationId,
                Amount = request.Amount,
                RequestedAt = dateTimeNow
            }, cancellationToken);
        }
    }, cancellationToken);
    
    return Results.Created($"/payments/{request.CorrelationId}", request);
});

app.MapGet("/payments-summary", async ([AsParameters] PaymentSummaryRequest request, IConnectionMultiplexer connectionMultiplexer) =>
{
    double fromScore = 0;
    double toScore = 0;
    RedisValue[] resultsDefault = {};
    RedisValue[] resultsFallback = {};
    
    fromScore = request != null && request.From != null && request.From > DateTime.MinValue ? new DateTimeOffset((DateTime)request.From).ToUnixTimeSeconds() : 0;
    toScore = request != null && request.To != null && request.To > DateTime.MinValue ? new DateTimeOffset((DateTime)request.To).ToUnixTimeSeconds() : 0;

    if (fromScore > toScore)
        return Results.BadRequest("Arruma esse filtro ae seu desgraça, tá mandando from maior do que to");

    var db = connectionMultiplexer.GetDatabase();

    if (fromScore > 0 && toScore == 0)
    {
        resultsDefault = await db.SortedSetRangeByScoreAsync("payment-requests-default", fromScore);
        resultsFallback = await db.SortedSetRangeByScoreAsync("payment-requests-fallback", fromScore);
    }
    else if (fromScore > 0 && toScore > 0)
    {
        resultsDefault = await db.SortedSetRangeByScoreAsync("payment-requests-default", fromScore, toScore);
        resultsFallback = await db.SortedSetRangeByScoreAsync("payment-requests-fallback",fromScore, toScore);
    }
    else
    {
        resultsDefault = await db.SortedSetRangeByScoreAsync("payment-requests-default");
        resultsFallback = await db.SortedSetRangeByScoreAsync("payment-requests-fallback");
    }

    return Results.Ok(new PaymentSummaryResponse
    {
        Default = new PaymentSummaryTypeProcessorResponse
        {
            TotalRequests = resultsDefault.Length,
            TotalAmount = resultsDefault.ToList().Sum(x => JsonSerializer.Deserialize<PaymentRequest>(x).Amount)
        },
        Fallback = new PaymentSummaryTypeProcessorResponse
        {
            TotalRequests = resultsFallback.Length,
            TotalAmount = resultsFallback.ToList().Sum(x => JsonSerializer.Deserialize<PaymentRequest>(x).Amount)
        },
    });
});

app.Run();

public record PaymentRequest
{
    public Guid CorrelationId { get; set; }
    public decimal Amount { get; set; }
}

internal record PaymentSummaryRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
internal record PaymentSummaryResponse
{
    public PaymentSummaryTypeProcessorResponse Default { get; set; }
    public PaymentSummaryTypeProcessorResponse Fallback { get; set; }
}

internal record PaymentSummaryTypeProcessorResponse
{
    public int TotalRequests { get; set; }
    public decimal TotalAmount { get; set; }
}
using MandiocaCozidinha.Services.Api.Configurations;
using MandiocaCozidinha.Services.Api.Consumers;
using MandiocaCozidinha.Services.Api.Contracts;
using MandiocaCozidinha.Services.Api.HostedServices;
using MandiocaCozidinha.Services.Api.Services;
using MassTransit;
using Microsoft.Extensions.Caching.Memory;
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

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration["Redis:Server"] ?? throw new InvalidOperationException("redis error");
    return ConnectionMultiplexer.Connect(configuration);
});

builder.Services.AddMassTransit(x =>
{
    x.DisableUsageTelemetry();
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<PaymentProcessorRequestConsumer, PaymentProcessorRequestConsumerDefinition>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Uri"], "/", c =>
        {
            c.Username(builder.Configuration["RabbitMq:User"]);
            c.Password(builder.Configuration["RabbitMq:Password"]);
        });
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.UseOpenApiWithScalarConfig();
app.UseHttpsRedirection();

app.MapPost("/payments", async (PaymentRequest request, IPaymentProcessorService paymentProcessorService, IBus bus) =>
{
    try
    {
        await paymentProcessorService.SendPaymentWithFailoverAsync(new PaymentProcessorRequest
        {
            CorrelationId = request.CorrelationId,
            Amount = request.Amount,
            RequestedAt = DateTime.UtcNow
        });
    }
    catch(InvalidOperationException ex)
    {
        await bus.GetSendEndpoint(new Uri("queue:payment-processor-request"))
            .ContinueWith(async endpoint =>
            {
                var sendEndpoint = await endpoint;
                await sendEndpoint.Send(new PaymentProcessorRequest
                {
                    CorrelationId = request.CorrelationId,
                    Amount = request.Amount,
                    RequestedAt = DateTime.UtcNow
                });
            });
    }
    
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
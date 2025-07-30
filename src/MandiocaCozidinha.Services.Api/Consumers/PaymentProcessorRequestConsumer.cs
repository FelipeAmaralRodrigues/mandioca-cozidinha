using MandiocaCozidinha.Services.Api.Contracts;
using MandiocaCozidinha.Services.Api.Services;
using MassTransit;

namespace MandiocaCozidinha.Services.Api.Consumers
{
    public class PaymentProcessorRequestConsumer : IConsumer<PaymentProcessorRequest>
    {
        private readonly IPaymentProcessorService _paymentProcessorService;

        public PaymentProcessorRequestConsumer(IPaymentProcessorService paymentProcessorService)
        {
            _paymentProcessorService = paymentProcessorService;
        }

        public async Task Consume(ConsumeContext<PaymentProcessorRequest> context)
        {
            await _paymentProcessorService.SendPaymentWithFailoverAsync(context.Message, context.CancellationToken);
        }
    }

    public class PaymentProcessorRequestConsumerDefinition : ConsumerDefinition<PaymentProcessorRequestConsumer>
    {
        public PaymentProcessorRequestConsumerDefinition()
        {
            EndpointName = "payment-processor-request";
        }
        protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<PaymentProcessorRequestConsumer> consumerConfigurator)
        {
            endpointConfigurator.UseMessageRetry(r => r.Interval(20, TimeSpan.FromSeconds(2)));
        }
    }
}

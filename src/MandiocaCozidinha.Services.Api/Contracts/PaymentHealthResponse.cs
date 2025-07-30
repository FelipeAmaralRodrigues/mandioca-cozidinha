namespace MandiocaCozidinha.Services.Api.Contracts
{
    public record PaymentHealthResponse
    {
        public bool TooManyRequests { get; set; } = false;
        public bool Failing { get; set; }
        public int MinResponseTime { get; set; }
    }
}

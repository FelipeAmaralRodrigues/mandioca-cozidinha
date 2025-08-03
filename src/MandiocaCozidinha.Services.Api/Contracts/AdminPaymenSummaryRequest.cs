namespace MandiocaCozidinha.Services.Api.Contracts
{
    public record AdminPaymenSummaryRequest
    {
        public decimal TotalAmount { get; set; }
        public int TotalRequests { get; set; }
        public decimal TotalFee { get; set; }
        public decimal FeePerTransaction { get; set; }
    }
}

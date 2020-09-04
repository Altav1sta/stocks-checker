using System;

namespace TestConsole
{
    public class StockQuote
    {
        public string Ticker { get; set; }
        
        public string Figi { get; set; }

        public decimal TinkoffBidPrice { get; set; }

        public decimal TinkoffBidSize { get; set; }

        public decimal TinkoffAskPrice { get; set; }

        public decimal TinkoffAskSize { get; set; }

        public decimal TinkoffMarketPrice { get; set; }

        public DateTime TinkoffUpdatedAt { get; set; }

        public string BidExchange { get; set; }

        public decimal BidPrice { get; set; }

        public decimal BidSize { get; set; }

        public string AskExchange { get; set; }

        public decimal AskPrice { get; set; }

        public decimal AskSize { get; set; }

        public DateTime UpdatedAt { get; set; }

        public decimal LongDelta { get; set; }

        public decimal ShortDelta { get; set; }

        public decimal LongDeltaPercent { get; set; }

        public decimal ShortDeltaPercent { get; set; }
    }
}

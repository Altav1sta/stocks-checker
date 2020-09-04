using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Tinkoff.Trading.OpenApi.Models;
using Tinkoff.Trading.OpenApi.Network;

namespace TestConsole
{
    internal sealed class TinkoffClient : IDisposable
    {
        public const int StockQuoteDepth = 1;
        public IDictionary<string, StockQuote> Quotes { get; } =
            new ConcurrentDictionary<string, StockQuote>();

        private readonly Connection _connection;
        private readonly Context _context;
        private readonly IDictionary<string, string> _tickersByFigi =
            new ConcurrentDictionary<string, string>();

        public TinkoffClient(string token)
        {
            _connection = ConnectionFactory.GetConnection(token);
            _context = _connection.Context;
            _context.StreamingEventReceived += StreamingEventReceivedHandler;
            _context.WebSocketException += WebSocketExceptionHandler;
            _context.StreamingClosed += StreamingClosedHandler;
        }


        public async Task SubscribeForAllQuotesAsync()
        {
            var stocks = await _context.MarketStocksAsync().ConfigureAwait(false);

            foreach (var instrument in stocks.Instruments)
            {
                if (instrument.Currency != Currency.Usd) continue;
                if (instrument.Ticker.Any(x => !char.IsUpper(x) || !char.IsLetter(x))) continue;

                var request = new StreamingRequest.OrderbookSubscribeRequest(
                    instrument.Figi, StockQuoteDepth, instrument.Ticker);

                _tickersByFigi[instrument.Figi] = instrument.Ticker;
                
                Quotes[instrument.Ticker] = new StockQuote
                {
                    Figi = instrument.Figi,
                    Ticker = instrument.Ticker,
                    TinkoffUpdatedAt = DateTime.MinValue,
                    UpdatedAt = DateTime.MinValue
                };

                await _context.SendStreamingRequestAsync(request).ConfigureAwait(false);
            }
        }

        private async void StreamingEventReceivedHandler(object sender, StreamingEventReceivedEventArgs args)
        {
            if (args.Response is OrderbookResponse response)
            {
                var ticker = _tickersByFigi[response.Payload.Figi];

                if (!Quotes.ContainsKey(ticker))
                {
                    Debug.WriteLine($"Received unexpected Tinkoff orderbook for ticker \"{ticker}\"");
                    var request = StreamingRequest.UnsubscribeOrderbook(response.Payload.Figi, StockQuoteDepth);
                    await _context.SendStreamingRequestAsync(request).ConfigureAwait(false);
                    return;
                }

                var quote = Quotes[ticker];

                if (response.Payload.Bids.Count > 0)
                {
                    quote.TinkoffBidPrice = response.Payload.Bids[0][0];
                    quote.TinkoffBidSize = response.Payload.Bids[0][1];
                }

                if (response.Payload.Asks.Count > 0)
                {
                    quote.TinkoffAskPrice = response.Payload.Asks[0][0];
                    quote.TinkoffAskSize = response.Payload.Asks[0][1];
                }

                quote.TinkoffMarketPrice =
                    (quote.TinkoffBidPrice * quote.TinkoffBidSize + quote.TinkoffAskPrice * quote.TinkoffAskSize) /
                    (quote.TinkoffBidSize + quote.TinkoffAskSize);

                quote.TinkoffUpdatedAt = response.Time;
                quote.LongDelta = quote.BidPrice - quote.TinkoffAskPrice;
                quote.ShortDelta = quote.TinkoffBidPrice - quote.AskPrice;

                quote.LongDeltaPercent = quote.LongDelta * 100 / quote.TinkoffMarketPrice;
                quote.ShortDeltaPercent = quote.ShortDelta * 100 / quote.TinkoffMarketPrice;
            }
        }

        private void WebSocketExceptionHandler(object sender, WebSocketException e)
        {
            Debug.WriteLine("An error occured");
            Debug.WriteLine("Error code: " + e.ErrorCode + " WebSocket error code: " + e.WebSocketErrorCode);
            Debug.WriteLine("Message: " + e.Message);
        }

        private void StreamingClosedHandler(object sender, EventArgs args)
        {
            Debug.WriteLine("Streaming closed");
        }

        public void Dispose()
        {
            _context?.Dispose();
            _connection?.Dispose();
        }
    }
}

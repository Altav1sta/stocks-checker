using Alpaca.Markets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace TestConsole
{
    internal sealed class AlpacaClient : IDisposable
    {
        public const int AlpacaMaxChannels = 30;
        public const int AlpacaRequestInterval = 350;

        public static IDictionary<long, string> ExchangesTapeCodes { get; } =
            new Dictionary<long, string>
            {
                { 2, "B" },
                { 9, "M" },
                { 17, "X" }
            };

        public int CurrentIndex
        {
            get => _currentIndex;
            set
            {
                _currentIndex = value == _quotes.Keys.Count ? 0 : value;
            }
        }

        public ICollection<string> SubscibedTickers  => _subscriptions.Keys;

        private readonly IDictionary<string, StockQuote> _quotes;
        private readonly IDictionary<string, IAlpacaDataSubscription> _subscriptions =
            new ConcurrentDictionary<string, IAlpacaDataSubscription>();

        private readonly AlpacaDataClient _dataClient;
        private readonly AlpacaDataStreamingClient _dataStreamingClient;

        private int _currentIndex = 0;
        private Timer _timer;
        private AuthStatus _authStatus;

        public AlpacaClient(string keyId, string secret, IDictionary<string, StockQuote> quotes)
        {
            var securityKey = new SecretKey(keyId, secret);

            _dataClient = Environments.Paper.GetAlpacaDataClient(securityKey);
            _dataStreamingClient = Environments.Paper.GetAlpacaDataStreamingClient(securityKey);
            _quotes = quotes;
        }


        public async Task ConnectAsync()
        {
            if (_authStatus == AuthStatus.Unauthorized)
            {
                _authStatus = await _dataStreamingClient.ConnectAndAuthenticateAsync().ConfigureAwait(false);
            }
        }

        public async Task DisconnectAsync()
        {
            if (_authStatus == AuthStatus.Authorized)
            {
                await _dataStreamingClient.DisconnectAsync().ConfigureAwait(false);
                _authStatus = AuthStatus.Unauthorized;
            }
        }

        public void StartRequestingQuotes()
        {
            if (_timer == null) _timer = new Timer(AlpacaRequestInterval);

            _timer.Elapsed += RequestQuoteAsync;
            _timer.AutoReset = true;

            _timer.Start();
        }

        public void StopRequestingQuotes()
        {
            _timer?.Stop();
        }

        public void SubscribeTicker(string ticker)
        {
            if (_subscriptions.Count == AlpacaMaxChannels)
            {
                Debug.WriteLine($"Could not subscribe to \"{ticker}\". All channels are already used.");
                return;
            }

            var subscription = _dataStreamingClient.GetQuoteSubscription(ticker);
            subscription.Received += QuoteReceivedHandler;
            _subscriptions[ticker] = subscription;
            _dataStreamingClient.Subscribe(subscription);
        }

        public void UnsubscribeTicker(string ticker)
        {
            if (!_subscriptions.ContainsKey(ticker))
            {
                Debug.WriteLine($"An attempt to unsubscribe not subscribed ticker \"{ticker}\".");
                return;
            }

            _dataStreamingClient.Unsubscribe(_subscriptions[ticker]);
            _subscriptions.Remove(ticker);
        }

        private void QuoteReceivedHandler(IStreamQuote streamQuote)
        {
            if (!_quotes.ContainsKey(streamQuote.Symbol))
            {
                Debug.WriteLine($"Received unexpected Alpaca quote for ticker \"{streamQuote.Symbol}\"");
                UnsubscribeTicker(streamQuote.Symbol);
                return;
            }

            var quote = _quotes[streamQuote.Symbol];

            if (ExchangesTapeCodes.ContainsKey(streamQuote.BidExchange))
            {
                quote.BidExchange = ExchangesTapeCodes[streamQuote.BidExchange];
                quote.BidPrice = streamQuote.BidPrice;
                quote.BidSize = streamQuote.BidSize;
                quote.LongDelta = quote.BidPrice - quote.TinkoffAskPrice;
                quote.UpdatedAt = streamQuote.Time;
                quote.LongDeltaPercent = quote.TinkoffMarketPrice == 0
                    ? 0
                    : quote.LongDelta * 100 / quote.TinkoffMarketPrice;
            }

            if (ExchangesTapeCodes.ContainsKey(streamQuote.AskExchange))
            {
                quote.AskExchange = ExchangesTapeCodes[streamQuote.AskExchange];
                quote.AskPrice = streamQuote.AskPrice;
                quote.AskSize = streamQuote.AskSize;
                quote.ShortDelta = quote.TinkoffBidPrice - quote.AskPrice;
                quote.UpdatedAt = streamQuote.Time;
                quote.ShortDeltaPercent = quote.TinkoffMarketPrice == 0
                    ? 0
                    : quote.ShortDelta * 100 / quote.TinkoffMarketPrice;
            }
        }

        private async void RequestQuoteAsync(object sender, ElapsedEventArgs e)
        {
            var tickers = _quotes.Keys.OrderBy(x => x).ToArray();

            for (; ; CurrentIndex++) if (!_subscriptions.ContainsKey(tickers[CurrentIndex])) break;

            try
            {
                var quote = await _dataClient.GetLastQuoteAsync(tickers[CurrentIndex]).ConfigureAwait(false);

                if (quote.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
                    QuoteReceivedHandler(quote);

                CurrentIndex++;
            }
            catch (RestClientErrorException ex)
            {
                Debug.WriteLine($"Error in REST API ticker \"{tickers[CurrentIndex]}\" request.");
                Debug.WriteLine($"Message: {ex.Message}");

                _quotes.Remove(tickers[CurrentIndex]);

                if (CurrentIndex == tickers.Length) CurrentIndex = 0;
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Close();
            _timer?.Dispose();
            _dataClient?.Dispose();
            _dataStreamingClient?.Dispose();
        }
    }
}

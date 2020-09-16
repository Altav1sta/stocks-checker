using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Tinkoff.Trading.OpenApi.Models;
using Tinkoff.Trading.OpenApi.Network;

namespace WebApp
{
    internal sealed class TinkoffClient : IDisposable
    {
        public const int StockQuoteDepth = 1;
        public const double CapPercentForStep = 5;
        public const double StepPercentForDelta = 3;
        public const double MinPriceForSignal = 1;
        public const int SignalIntervalMinutes = 30;
        public IDictionary<string, CandlePayload> Candles { get; } =
            new ConcurrentDictionary<string, CandlePayload>();
        public IDictionary<string, DateTime> NextSignalTime { get; } =
            new ConcurrentDictionary<string, DateTime>();

        private readonly Credentials _credentials;
        private readonly ILogger _logger;
        private readonly Bot _bot;
        private readonly IDictionary<string, string> _tickersByFigi =
            new ConcurrentDictionary<string, string>();

        private Connection _connection;
        private Context _context;

        public TinkoffClient(IOptions<Credentials> options, ILogger<TinkoffClient> logger, Bot bot)
        {
            _credentials = options.Value;
            _logger = logger;
            _bot = bot;

            Initialize();

            _bot.RunAsync().GetAwaiter().GetResult();
        }


        public async Task SubscribeForAllCandles()
        {
            _logger.LogInformation("Subscribed for candles");

            var stocks = await _context.MarketStocksAsync();

            foreach (var instrument in stocks.Instruments)
            {
                if (instrument.Currency != Currency.Usd) continue;
                if (instrument.Ticker.Any(x => !char.IsUpper(x) || !char.IsLetter(x))) continue;

                var request = new StreamingRequest.CandleSubscribeRequest(
                    instrument.Figi, CandleInterval.Minute, instrument.Ticker);

                _tickersByFigi[instrument.Figi] = instrument.Ticker;

                await _context.SendStreamingRequestAsync(request);
            }
        }

        private void Initialize()
        {
            _context?.Dispose();
            _connection?.Dispose();
            _connection = ConnectionFactory.GetConnection(_credentials.TinkoffToken);
            _context = _connection.Context;
            _context.StreamingEventReceived += StreamingEventReceivedHandler;
            _context.WebSocketException += WebSocketExceptionHandler;
            _context.StreamingClosed += StreamingClosedHandler;
        }

        private async void StreamingEventReceivedHandler(object sender, StreamingEventReceivedEventArgs args)
        {
            switch (args.Response)
            {
                case CandleResponse response:

                    var ticker = _tickersByFigi[response.Payload.Figi];
                    var currentValue = (double)response.Payload.Close;
                    var previousValue = (double?)null;

                    if (!Candles.ContainsKey(ticker))
                    {
                        Candles[ticker] = response.Payload;
                    }
                    else
                    {
                        previousValue = (double?)Candles[ticker].Close;
                    }

                    if (!NextSignalTime.ContainsKey(ticker) || NextSignalTime[ticker] < DateTime.UtcNow)
                    {
                        var signalValue = GetSignalValue(currentValue, previousValue);

                        if (signalValue.HasValue)
                        {
                            var success = await _bot.SendLevelSignalAsync(ticker, signalValue.Value);

                            if (success) NextSignalTime[ticker] = DateTime.UtcNow.AddMinutes(SignalIntervalMinutes);
                        }
                    }
                    break;

                case StreamingErrorResponse response:
                    _logger.LogError("Error response received: {StreamingErrorResponse}", response);
                    break;

                default:
                    _logger.LogError("Received unrecognized type of streaming response. {StreamingResponse}", args.Response);
                    break;
            }
        }

        private void WebSocketExceptionHandler(object sender, WebSocketException e)
        {
            _logger.LogError(e, "Web socket error occured");

            Initialize();
        }

        private void StreamingClosedHandler(object sender, EventArgs args)
        {
            _logger.LogInformation("Tinkoff streaming closed");
        }

        private double? GetSignalValue(double currentValue, double? previousValue)
        {
            if (currentValue < MinPriceForSignal) return null;
            
            var step = GetLevelStep(currentValue);
            var signalDelta = step * StepPercentForDelta / 100;
            var closestLevel = GetClosestLevel(currentValue, step);
            var currentDistance = closestLevel - currentValue;

            if (Math.Abs(currentDistance) < signalDelta)
            {
                if (!previousValue.HasValue) return closestLevel;

                var previousDistance = closestLevel - previousValue.Value;

                // if previous value didn't trigger signal
                if ((Math.Abs(previousDistance) >= signalDelta) && 
                    (Math.Sign(previousDistance) == Math.Sign(currentDistance)))
                    return closestLevel;
            }

            return null;
        }

        private double GetLevelStep(double value)
        {
            return Math.Pow(10, Math.Ceiling(Math.Log10(value))) * CapPercentForStep / 100;
        }

        private double GetClosestLevel(double value, double step)
        {
            return step * Math.Round(value / step, MidpointRounding.ToPositiveInfinity);
        }

        public void Dispose()
        {
            _logger.LogInformation("Tinkoff client disposed");
            _bot?.StopAsync().GetAwaiter().GetResult();
            _context?.Dispose();
            _connection?.Dispose();
        }
    }
}

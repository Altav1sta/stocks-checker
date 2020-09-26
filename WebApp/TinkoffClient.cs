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
using WebApp.Models;

namespace WebApp
{
    internal sealed class TinkoffClient
    {
        public const int StockQuoteDepth = 1;
        public const double CapPercentForStep = 5;
        public const double StepPercentForDelta = 3;
        public const double MinPriceForSignal = 1;

        public event EventHandler<SendingSignalEventArgs> SendingSignalEvent;

        public IDictionary<string, CandlePayload> Candles { get; } =
            new ConcurrentDictionary<string, CandlePayload>();
        public IDictionary<string, DateTime> NextSignalTime { get; } =
            new ConcurrentDictionary<string, DateTime>();

        private readonly Settings _settings;
        private readonly ILogger _logger;
        private readonly IContext _context;
        private readonly IDictionary<string, string> _tickersByFigi =
            new ConcurrentDictionary<string, string>();

        public TinkoffClient(IOptions<Settings> options, ILogger<TinkoffClient> logger)
        {
            _settings = options.Value;
            _logger = logger;

            if (_settings.Mode.Equals("Test", StringComparison.OrdinalIgnoreCase))
            {
                var connection = ConnectionFactory.GetSandboxConnection(_settings.TinkoffSandboxToken);
                _context = connection.Context;
            }
            else
            {
                var connection = ConnectionFactory.GetConnection(_settings.TinkoffToken);
                _context = connection.Context;
            }

            _context.StreamingEventReceived += StreamingEventReceivedHandler;
            _context.WebSocketException += WebSocketExceptionHandler;
            _context.StreamingClosed += StreamingClosedHandler;
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

        private void StreamingEventReceivedHandler(object sender, StreamingEventReceivedEventArgs args)
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

                        if (signalValue.HasValue) RaiseSendingSignalEvent(ticker, signalValue.Value);
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
        }

        private void StreamingClosedHandler(object sender, EventArgs args)
        {
            _logger.LogInformation("Tinkoff streaming closed");
        }

        private void RaiseSendingSignalEvent(string ticker, double value)
        {
            var handler = SendingSignalEvent;
            var args = new SendingSignalEventArgs { Ticker = ticker, Value = value };
            handler?.Invoke(this, args);
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
    }
}

using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestConsole
{
    class Program
    {
        private static decimal _maxPrice = 40;        
        private static bool _sortByLong = true;
        private static int _pageSize = 20;

        public static async Task Main(string[] args)
        {
            var cfg = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .AddUserSecrets(typeof(Program).Assembly)
                .Build();

            Console.SetWindowSize(Console.LargestWindowWidth * 4 / 5, Console.LargestWindowHeight * 4 / 5);

            var tokenTinkoff = cfg["Tinkoff:Token"];
            var alpacaKeyId = cfg["Alpaca:ApiKeyId"];
            var alpacaSecret = cfg["Alpaca:Secret"];

            using var tinkoffClient = new TinkoffClient(tokenTinkoff);
            
            await tinkoffClient.SubscribeForAllQuotesAsync().ConfigureAwait(false);

            using var alpacaClient = new AlpacaClient(alpacaKeyId, alpacaSecret, tinkoffClient.Quotes);

            await alpacaClient.ConnectAsync().ConfigureAwait(false);

            alpacaClient.StartRequestingQuotes();

            while (true)
            {
                PrintTickers(tinkoffClient.Quotes, alpacaClient.SubscibedTickers);
                
                Console.WriteLine();
                Console.WriteLine("LIST OF COMMANDS:");
                Console.WriteLine();
                Console.WriteLine("<ENTER> - reprint table");
                Console.WriteLine("/sub <ticker> - subscribe for ticker by web socket");
                Console.WriteLine("/unsub <ticker> - unsubscribe from ticker");
                Console.WriteLine("/sort {short/long} - set the direction of sorting");
                Console.WriteLine("/pricelim <price> - set upper limit for equity price");
                Console.WriteLine("/pagesize <size> - set ticker table page size");
                Console.WriteLine("/quit - quit the app");

                var command = Console.ReadLine().Trim();

                if (command.Equals("/quit", StringComparison.Ordinal)) break;

                if (command.StartsWith("/sub ", StringComparison.Ordinal))
                {
                    var ticker = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                    alpacaClient.SubscribeTicker(ticker);
                    continue;
                }

                if (command.StartsWith("/unsub ", StringComparison.Ordinal))
                {
                    var ticker = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                    alpacaClient.UnsubscribeTicker(ticker);
                    continue;
                }

                if (command.Equals("/sort short", StringComparison.Ordinal))
                {
                    _sortByLong = false;
                    continue;
                }

                if (command.Equals("/sort long", StringComparison.Ordinal))
                {
                    _sortByLong = true;
                    continue;
                }

                if (command.StartsWith("/pricelim ", StringComparison.Ordinal))
                {
                    var limitString = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                    decimal.TryParse(limitString, out _maxPrice);
                    continue;
                }

                if (command.StartsWith("/pagesize ", StringComparison.Ordinal))
                {
                    var sizeString = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                    int.TryParse(sizeString, out _pageSize);
                    continue;
                }
            }

            alpacaClient.StopRequestingQuotes();
            await alpacaClient.DisconnectAsync().ConfigureAwait(false);
        }

        private static void PrintTickers(IDictionary<string, StockQuote> quotes, ICollection<string> subscriptions)
        {
            var updatedQuotes = quotes.Values
                .Where(x => x.TinkoffUpdatedAt > DateTime.MinValue && x.UpdatedAt > DateTime.MinValue)
                .Where(x => x.TinkoffAskPrice < _maxPrice);

            var sortedQuotes = _sortByLong
                ? updatedQuotes.OrderByDescending(x => x.LongDeltaPercent)
                : updatedQuotes.OrderByDescending(x => x.ShortDeltaPercent);

            sortedQuotes = sortedQuotes.OrderByDescending(x => x.BidPrice - x.TinkoffMarketPrice);

            Console.Clear();
            Console.WriteLine("TICKERS TABLE\n");
            Console.WriteLine(
                "Ticker\tTBid\tSize\tTAsk\tSize\tExchs\tBid\tSize\tAsk\tSize\tTime\t\tLong\t%\tShort\t%");
            Console.WriteLine(
                "======\t======\t======\t======\t======\t=======\t======\t======\t======\t======\t=============\t======\t======\t======");

            foreach (var quote in sortedQuotes.Take(_pageSize).ToList())
            {
                Console.WriteLine(
                    $"{quote.Ticker}\t{quote.TinkoffBidPrice}\t{quote.TinkoffBidSize}\t" +
                    $"{quote.TinkoffAskPrice}\t{quote.TinkoffAskSize}\t" +
                    $"{quote.BidExchange ?? "-"}:{quote.AskExchange ?? "-"}\t" +
                    $"{ValueString(quote.BidPrice)}\t{ValueString(quote.BidSize)}\t" +
                    $"{ValueString(quote.AskPrice)}\t{ValueString(quote.AskSize)}\t" +
                    $"{quote.UpdatedAt.ToLocalTime():dd.MM HH:mm:ss}\t" +
                    $"{(quote.BidPrice == 0 ? "-" : quote.LongDelta.ToString())}\t" +
                    $"{(quote.BidPrice == 0 ? "-" : quote.LongDeltaPercent.ToString("00.##"))}\t" +
                    $"{(quote.AskSize == 0 ? "-" : quote.ShortDelta.ToString())}\t" +
                    $"{(quote.AskSize == 0 ? "-" : quote.ShortDeltaPercent.ToString("00.##"))}\t");
            }

            Console.WriteLine();
            Console.WriteLine("TICKERS SUBSCRIBED: " + subscriptions.Count);
            if (subscriptions.Count > 0) Console.WriteLine(string.Join(", ", subscriptions));
            Console.WriteLine();

            Console.WriteLine("SORTED BY: " + (_sortByLong ? "long" : "short"));
            Console.WriteLine();

            Console.WriteLine("MAX EQUITY PRICE ($): " + _maxPrice);
            Console.WriteLine();

            Console.WriteLine("TABLE PAGE SIZE: " + _pageSize);
            Console.WriteLine();
        }

        private static string ValueString(decimal n) => n == 0 ? "-" : $"{n}";
    }
}

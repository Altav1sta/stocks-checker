using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using WebApp.Models;

namespace WebApp
{
    internal sealed class Bot : IAsyncDisposable
    {
        public const int GlobalSignalIntervalSeconds = 30;
        public const int SignalIntervalSeconds = 30 * 60;

        private readonly ITelegramBotClient _telegramClient;
        private readonly TinkoffClient _tinkoffClient;
        private readonly Settings _settings;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly DateTime _startTime;

        private DateTime _nextSignalTime;
        private bool _disposed;

        public Bot(IOptions<Settings> options, IServiceScopeFactory scopeFactory, TinkoffClient tinkoffClient, ILogger<Bot> logger)
        {
            _logger = logger;
            _settings = options.Value;
            _scopeFactory = scopeFactory;
            _tinkoffClient = tinkoffClient;
            _tinkoffClient.SendingSignalEvent += OnSendingSignalAsync;
            _telegramClient = new TelegramBotClient(_settings.BotToken);
            _telegramClient.OnMessage += OnMessage;
            _startTime = DateTime.Now;
            _nextSignalTime = DateTime.UtcNow.AddSeconds(GlobalSignalIntervalSeconds);
        }

        public async Task RunAsync()
        {
            await SendMessageAsync("Bot started!");
            await _tinkoffClient.SubscribeForAllCandles();

            _telegramClient.StartReceiving();
        }

        private async void OnSendingSignalAsync(object sender, SendingSignalEventArgs args)
        {
            try
            {
                if (_nextSignalTime > DateTime.UtcNow) return;

                using var scope = _scopeFactory.CreateScope();
                using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

                var chats = await context.Chats.ToListAsync();
                chats = FilterForMode(chats);

                foreach (var chat in chats)
                {
                    try
                    {
                        _nextSignalTime = DateTime.UtcNow.AddSeconds(GlobalSignalIntervalSeconds);
                        await _telegramClient.SendTextMessageAsync(chat.Id, $"{args.Ticker} near the level {args.Value}");
                    }
                    catch (ApiRequestException ex)
                    {
                        if (ex.ErrorCode == 403)
                        {
                            var record = await context.Chats.FirstOrDefaultAsync(x => x.Id == chat.Id);
                            if (record != null) context.Remove(record);
                            await context.SaveChangesAsync();
                        }
                    }
                }

                _tinkoffClient.NextSignalTime[args.Ticker] = DateTime.UtcNow.AddSeconds(SignalIntervalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occured while sending signal for ticker {Ticker}", args.Ticker);
            }
        }

        private async void OnMessage(object sender, MessageEventArgs e)
        {
            try
            {
                var chatId = e.Message.Chat.Id;
                await _telegramClient.SendTextMessageAsync(chatId, $"Bot is up and running since {_startTime}");

                using var scope = _scopeFactory.CreateScope();
                using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

                var isAdded = await context.Chats.AnyAsync(x => x.Id == chatId);

                if (!isAdded)
                {
                    await context.Chats.AddAsync(new Chat
                    {
                        Id = chatId,
                        Text = e.Message.From.Username,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });

                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing message {MessageEventArgs}", e);
            }
        }

        private async Task SendMessageAsync(string text)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

                var chats = await context.Chats.ToListAsync();
                chats = FilterForMode(chats);

                foreach (var chat in chats)
                {
                    try
                    {
                        await _telegramClient.SendTextMessageAsync(chat.Id, text);
                    }
                    catch (ApiRequestException ex)
                    {
                        if (ex.ErrorCode == 403)
                        {
                            var record = await context.Chats.FirstOrDefaultAsync(x => x.Id == chat.Id);
                            if (record != null) context.Remove(record);
                            await context.SaveChangesAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occured while sending message");
            }
        }

        private List<Chat> FilterForMode(List<Chat> chats)
        {
            if (_settings.Mode.Equals("Test", StringComparison.OrdinalIgnoreCase)) 
                return chats;
            
            return chats.Where(x => x.Text.Contains(_settings.BotAuthor, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            _telegramClient.StopReceiving();
            await SendMessageAsync("Bot is shutting down :(");
            _disposed = true;
        }
    }
}

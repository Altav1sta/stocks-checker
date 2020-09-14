﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;

namespace WebApp
{
    internal sealed class Bot
    {
        public const int SignalIntervalSeconds = 30;

        private readonly ITelegramBotClient _client;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly DateTime _startTime;

        private DateTime _nextSignalTime;

        public Bot(IOptions<Credentials> options, IServiceScopeFactory scopeFactory, ILogger<Bot> logger)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _client = new TelegramBotClient(options.Value.BotToken);
            _client.OnMessage += OnMessage;
            _startTime = DateTime.Now;
            _nextSignalTime = DateTime.UtcNow.AddSeconds(SignalIntervalSeconds);
        }

        public void Run()
        {
            _client.StartReceiving();
        }

        public void Stop()
        {
            _client.StopReceiving();
        }

        public async Task SendLevelSignalAsync(string ticker, double level)
        {
            try
            {
                if (_nextSignalTime > DateTime.UtcNow) return;

                using var scope = _scopeFactory.CreateScope();
                using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

                var chats = await context.Chats.ToListAsync();

                foreach (var chat in chats)
                {
                    try
                    {
                        _nextSignalTime = DateTime.UtcNow.AddSeconds(SignalIntervalSeconds);
                        await _client.SendTextMessageAsync(chat.Id, $"{ticker} near the level {level}");
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
                _logger.LogError(ex, "An error occured while sending signal for ticker {Ticker}", ticker);
            }
        }

        public async void OnMessage(object sender, MessageEventArgs e)
        {
            try
            {
                var chatId = e.Message.Chat.Id;
                await _client.SendTextMessageAsync(chatId, $"Bot is up and running since {_startTime}");

                using var scope = _scopeFactory.CreateScope();
                using var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

                var isAdded = await context.Chats.AnyAsync(x => x.Id == chatId);

                if (!isAdded && e.Message.From.Username.Contains("a1tav1sta"))
                {
                    await context.Chats.AddAsync(new Models.Chat
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
    }
}

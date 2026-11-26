using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Modules;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace DiscordBot
{
    class Program
    {
        static void Main() => new Program().RunAsync().GetAwaiter().GetResult();

        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;

        public async Task RunAsync()
        {
            var config = new DiscordSocketConfig()
            {
                EnableVoiceDaveEncryption = true,

                GatewayIntents = GatewayIntents.DirectMessages
                                 | GatewayIntents.GuildMessages
                                 | GatewayIntents.Guilds
                                 | GatewayIntents.MessageContent
                                 | GatewayIntents.GuildVoiceStates
            };

            _client = new DiscordSocketClient(config);

            _commands = new CommandService();

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .AddSingleton<AudioService>()
                .AddSingleton<PlaylistService>()
                .BuildServiceProvider();

            DotNetEnv.Env.Load();

            string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

            _client.Log += Client_Log;

            _client.Ready += Client_Ready;

            await InstallCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, token);

            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private Task Client_Ready()
        {
            Console.WriteLine($"Logged in as {_client.CurrentUser}");

            return Task.CompletedTask;
        }

        private Task Client_Log(LogMessage arg)
        {
            Console.WriteLine(arg);

            return Task.CompletedTask;
        }

        public async Task InstallCommandsAsync()
        {
            _client.MessageReceived += HandleCommandAsync;

            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        private Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;

            if (message == null || message.Author.IsBot) return Task.CompletedTask;

            int argPos = 0;

            if (message.HasStringPrefix("!", ref argPos))
            {
                _ = ExecuteCommandAsync(message, argPos);
            }

            return Task.CompletedTask;
        }

        private async Task ExecuteCommandAsync(SocketUserMessage message, int argPos)
        {
            try
            {
                var context = new SocketCommandContext(_client, message);

                var result = await _commands.ExecuteAsync(context, argPos, _services);

                if (!result.IsSuccess)
                {
                    Console.WriteLine(result.ErrorReason);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Command execution error: {ex}");
            }
        }
    }
}
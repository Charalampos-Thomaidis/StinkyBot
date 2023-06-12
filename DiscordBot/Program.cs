using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

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
                GatewayIntents = GatewayIntents.DirectMessages
                                 | GatewayIntents.GuildMessages
                                 | GatewayIntents.Guilds
                                 | GatewayIntents.GuildMembers
                                 | GatewayIntents.MessageContent
                                 | GatewayIntents.GuildVoiceStates
            };

            _client = new DiscordSocketClient(config);

            _commands = new CommandService();

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .BuildServiceProvider();

            string token = "MTExNjQwNzQyMjk3ODg5MTg5MA.GtIGFD.GQ5fNOxONv7Pxd7TAJCqSC98jmz4sNW0Cm_MhI";

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

        private async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;
            var context = new SocketCommandContext(_client, message);

            if (message.Author.IsBot) return;

            int argPos = 0;
            if (message.HasStringPrefix("!", ref argPos))
            {
                var result = await _commands.ExecuteAsync(context, argPos, _services);
                if (!result.IsSuccess) Console.WriteLine(result.ErrorReason);
            }
        }
    }
}
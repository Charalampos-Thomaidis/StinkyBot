using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Audio;
using YoutubeExplode;
using YoutubeExplode.Search;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;

namespace DiscordBot.Modules
{
    public class Commands : ModuleBase<SocketCommandContext>
    {
        private readonly AudioService _audioService;

        public Commands(AudioService audioService)
        {
            _audioService = audioService;
        }

        [Command("song", RunMode = RunMode.Async)]
        public async Task HandlePlayCommand([Remainder] string arguments)
        {
            SocketVoiceState? botVoiceState = (Context.Guild as SocketGuild)?.CurrentUser?.VoiceState;

            if (botVoiceState?.VoiceChannel == null)
            {
                await ReplyAsync("I am not currently in a voice channel.");
                return;
            }

            await ReplyAsync("Searching for song, please wait...");

            var youtube = new YoutubeClient();

            IReadOnlyList<VideoSearchResult> searchResults = await youtube.Search.GetVideosAsync(arguments);

            VideoSearchResult video = searchResults.FirstOrDefault();

            StreamManifest streamInfoSet = await youtube.Videos.Streams.GetManifestAsync(video.Id);

            IStreamInfo audioStreamInfoWithHighestBitrate = streamInfoSet.GetAudioOnlyStreams().GetWithHighestBitrate();

            string filePath = $"{video.Id}.{audioStreamInfoWithHighestBitrate.Container.Name}";

            await youtube.Videos.Streams.DownloadAsync(audioStreamInfoWithHighestBitrate, filePath);

            string response = $"Now playing: {video.Title}\n{video.Url}";

            await ReplyAsync(response);

            await _audioService.SendAudio(Context.Client.GetGuild(Context.Guild.Id).AudioClient, filePath);
        }

        [Command("skip", RunMode = RunMode.Async)]
        public async Task HandleSkipCommand()
        {
            if (!_audioService.IsPlaying)
            {
                await ReplyAsync("There is no song currently playing.");
                return;
            }

            _audioService.StopAudio();

            await ReplyAsync("Skipped the current song.");
        }

        [Command("summon", RunMode = RunMode.Async)]
        public async Task HandleSummonCommand()
        {
            IVoiceChannel voiceChannel = (Context.User as IGuildUser)?.VoiceChannel;

            SocketVoiceChannel bvoiceChannel = (Context.Guild as SocketGuild)?.CurrentUser?.VoiceChannel;

            if (voiceChannel == null)
            {
                await ReplyAsync("You must be in a voice channel to use this command.");
                return;
            }

            if (bvoiceChannel == voiceChannel)
            {
                await ReplyAsync("I am currently in your voice channel.");
                return;
            }

            await voiceChannel.ConnectAsync();

            await ReplyAsync("Joined the voice channel.");
        }

        [Command("leave", RunMode = RunMode.Async)]
        public async Task HandleLeaveCommand()
        {
            SocketGuild guild = (Context.Client as DiscordSocketClient).GetGuild(Context.Guild.Id);

            IAudioClient audioClient = guild?.AudioClient;

            if (audioClient?.ConnectionState == ConnectionState.Disconnected)
            {
                await ReplyAsync("I am not currently in a voice channel.");
                return;
            }

            await audioClient.StopAsync();

            await ReplyAsync("Left the voice channel.");
        }

        [Command("commands")]
        public async Task HandleExplanationCommands()
        {
            await ReplyAsync("!song + name of the song (play song)\n!summon (ill join your vc)\n!leave (ill leave your vc)\n!skip (skip song)");
        }
    }
}
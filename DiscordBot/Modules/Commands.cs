using System.Threading.Tasks;
using System.Linq;
using Discord;
using Discord.Commands;
using YoutubeExplode;
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
            var voiceChannel = (Context.User as IGuildUser)?.VoiceChannel;

            await ReplyAsync("Searching for song, please wait...");

            if (voiceChannel == null)
            {
                await ReplyAsync("You must be in a voice channel to use this command.");
                return;
            }

            var youtube = new YoutubeClient();

            var searchResults = await youtube.Search.GetVideosAsync(arguments);

            var video = searchResults.FirstOrDefault();

            var streamInfoSet = await youtube.Videos.Streams.GetManifestAsync(video.Id);

            var audioStreamInfoWithHighestBitrate = streamInfoSet.GetAudioOnlyStreams().GetWithHighestBitrate();

            if (audioStreamInfoWithHighestBitrate == null)
            {
                await ReplyAsync("Failed to retrieve audio stream for the video.");
                return;
            }

            var filePath = $"{video.Id}.{audioStreamInfoWithHighestBitrate.Container.Name}";

            await youtube.Videos.Streams.DownloadAsync(audioStreamInfoWithHighestBitrate, filePath);

            var response = $"Now playing: {video.Title}\n{video.Url}";

            await ReplyAsync(response);

            await _audioService.PlayAudio(voiceChannel, filePath);
        }

        [Command("skip", RunMode = RunMode.Async)]
        public async Task SkipCommand()
        {
            await _audioService.StopAudio();

            await ReplyAsync("Skipped the current song.");
        }

        [Command("summon", RunMode = RunMode.Async)]
        public async Task HandleSummonBotCommand(IVoiceChannel voiceChannel = null)
        {
            voiceChannel ??= (Context.User as IGuildUser)?.VoiceChannel;

            if (voiceChannel == null)
            {
                await ReplyAsync("You must be in a voice channel to use this command.");
                return;
            }

            await ReplyAsync("Joined the voice channel.");

            await voiceChannel.ConnectAsync();
        }

        [Command("leave", RunMode = RunMode.Async)]
        public async Task HandleLeaveBotCommand(IVoiceChannel voiceChannel = null)
        {
            voiceChannel ??= (Context.User as IGuildUser)?.VoiceChannel;

            if (voiceChannel == null)
            {
                await ReplyAsync("The bot is not currently in a voice channel.");
                return;
            }

            await ReplyAsync("Left the voice channel.");

            await voiceChannel.DisconnectAsync();
        }

        [Command("commands")]
        public async Task HandleExplanationCommands()
        {
            await ReplyAsync("!song + name of the song (play song)\n!summon (ill join your vc)\n!leave (ill leave your vc)\n!skip (skip song)");
        }
    }
}
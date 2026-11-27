using Discord;
using Discord.Audio;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace DiscordBot.Modules
{
    public class Commands : ModuleBase<SocketCommandContext>
    {
        private readonly AudioService _audioService;
        private readonly PlaylistService _playlistService;

        private static readonly ConcurrentDictionary<ulong, PlaylistTrack> CurrentTracks = new ConcurrentDictionary<ulong, PlaylistTrack>();
        private static readonly ConcurrentDictionary<ulong, bool> ActivePlaylists = new ConcurrentDictionary<ulong, bool>();
        private static readonly ConcurrentDictionary<ulong, bool> StopPlaylistRequests = new ConcurrentDictionary<ulong, bool>();

        public Commands(AudioService audioService, PlaylistService playlistService)
        {
            _audioService = audioService;
            _playlistService = playlistService;

        }

        private async Task<string> DownloadPlaylistTrackAsync(PlaylistTrack track)
        {
            var youtube = new YoutubeClient();

            StreamManifest streamInfoSet = await youtube.Videos.Streams.GetManifestAsync(track.VideoId);

            IStreamInfo audioStreamInfoWithHighestBitrate = streamInfoSet.GetAudioOnlyStreams().GetWithHighestBitrate();

            string filePath = $"{track.VideoId}.{audioStreamInfoWithHighestBitrate.Container.Name}";

            await youtube.Videos.Streams.DownloadAsync(audioStreamInfoWithHighestBitrate, filePath);

            return filePath;
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

            if (ActivePlaylists.ContainsKey(Context.Guild.Id))
            {
                await ReplyAsync("A music list is currently playing.");
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

            var track = new PlaylistTrack
            {
                Title = video.Title,
                VideoId = video.Id.ToString(),
                Url = video.Url
            };

            CurrentTracks[Context.Guild.Id] = track;

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


        [Command("addurlsong", RunMode = RunMode.Async)]
        public async Task HandleAddUrlSongCommand([Remainder] string url)
        {
            try
            {
                var youtube = new YoutubeClient();

                VideoId videoId = VideoId.Parse(url);

                var video = await youtube.Videos.GetAsync(videoId);

                var track = new PlaylistTrack
                {
                    Title = video.Title,
                    VideoId = video.Id.ToString(),
                    Url = video.Url
                };

                await _playlistService.AddTrackAsync(Context.Guild.Id, track);

                await ReplyAsync($"Added to music list: **{track.Title}**");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Add URL song error: {ex}");

                await ReplyAsync("Could not add the song. Please provide a valid YouTube URL.");
            }
        }

        [Command("addsong", RunMode = RunMode.Async)]
        public async Task HandleAddToPlaylistCommand()
        {
            ulong guildId = Context.Guild.Id;

            if (!CurrentTracks.TryGetValue(guildId, out PlaylistTrack track))
            {
                await ReplyAsync("There is no song currently playing.");
                return;
            }

            await _playlistService.AddTrackAsync(guildId, track);

            await ReplyAsync($"Added to music list: **{track.Title}**");
        }

        [Command("removesong", RunMode = RunMode.Async)]
        public async Task HandleRemoveFromListCommand(int songNumber)
        {
            var playlist = await _playlistService.GetPlaylistAsync(Context.Guild.Id);

            if (playlist.Count == 0)
            {
                await ReplyAsync("The music list is empty.");
                return;
            }

            if (songNumber < 1 || songNumber > playlist.Count)
            {
                await ReplyAsync($"Invalid song number. Choose a number between 1 and {playlist.Count}.");
                return;
            }

            string removedSong = playlist[songNumber - 1].Title;

            await _playlistService.RemoveTrackAsync(Context.Guild.Id, songNumber - 1);

            await ReplyAsync($"Removed **{removedSong}** from the music list.");
        }

        [Command("playmusiclist", RunMode = RunMode.Async)]
        public async Task HandlePlayMusicPlaylistCommand()
        {
            ulong guildId = Context.Guild.Id;

            SocketVoiceState? botVoiceState = (Context.Guild as SocketGuild)?.CurrentUser?.VoiceState;

            if (botVoiceState?.VoiceChannel == null)
            {
                await ReplyAsync("I am not currently in a voice channel.");
                return;
            }

            if (_audioService.IsPlaying)
            {
                await ReplyAsync("A song is already playing.");
                return;
            }

            if (!ActivePlaylists.TryAdd(guildId, true))
            {
                await ReplyAsync("The playlist is already playing.");
                return;
            }

            try
            {
                List<PlaylistTrack> playlist = await _playlistService.GetPlaylistAsync(guildId);
                StopPlaylistRequests.TryRemove(guildId, out _);

                if (playlist.Count == 0)
                {
                    await ReplyAsync("Your music list is empty.");
                    return;
                }

                var youtube = new YoutubeClient();

                await ReplyAsync($"Starting music list with {playlist.Count} songs.");

                for (int i = 0; i < playlist.Count; i++)
                {
                    if (StopPlaylistRequests.TryRemove(guildId, out _))
                    {
                        await ReplyAsync("Music list stopped.");
                        break;
                    }

                    PlaylistTrack track = playlist[i];

                    await ReplyAsync($"Now playing ({i + 1}/{playlist.Count}): **{track.Title}**\n{track.Url}");

                    string filePath = await DownloadPlaylistTrackAsync(track);

                    CurrentTracks[guildId] = track;

                    try
                    {
                        await _audioService.SendAudio(Context.Client.GetGuild(guildId).AudioClient, filePath);
                    }
                    finally
                    {
                        CurrentTracks.TryRemove(guildId, out _);
                    }
                }

                await ReplyAsync("Music list finished!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Music list playback error: {ex}");
                await ReplyAsync("An error occurred while playing the music list.");
            }
            finally
            {
                ActivePlaylists.TryRemove(guildId, out _);
            }
        }

        [Command("stopmusiclist", RunMode = RunMode.Async)]
        public async Task HandleStopMusicListCommand()
        {
            ulong guildId = Context.Guild.Id;

            if (!ActivePlaylists.ContainsKey(guildId))
            {
                await ReplyAsync("There is no music list currently playing.");
                return;
            }

            StopPlaylistRequests[guildId] = true;

            if (_audioService.IsPlaying)
            {
                _audioService.StopAudio();
            }

            await ReplyAsync("Stopping the music list...");
        }


        [Command("musiclist", RunMode = RunMode.Async)]
        public async Task HandleShowMusicListCommand()
        {
            var playlist = await _playlistService.GetPlaylistAsync(Context.Guild.Id);

            if (playlist.Count == 0)
            {
                await ReplyAsync("The music list is empty.");
                return;
            }

            string message = "**Music List:**\n";

            for (int i = 0; i < playlist.Count; i++)
            {
                message += $"\n**{i + 1}.** {playlist[i].Title}\n{playlist[i].Url}\n";
            }

            await Context.Channel.SendMessageAsync(message, options: new RequestOptions(), allowedMentions: AllowedMentions.None, messageReference: null, flags: MessageFlags.SuppressEmbeds);
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
            await ReplyAsync(
                "!summon (ill join your vc)\n" +
                "!leave (ill leave your vc)\n" +
                "!song + name of the song (play song)\n" +
                "!skip (skip song or skip current song from music list)\n" +
                "!addurlsong + url of the song (add that url song to the music list)\n" +
                "!addsong (add current song to music list)\n" +
                "!removesong (remove current song from music list)\n" +
                "!playmusiclist (play saved music list)\n" +
                "!stopmusiclist (stop the entire music list)\n" +
                "!musiclist (shows the saved music music list)"
            );
        }

        [Command("testaudio", RunMode = RunMode.Async)]
        public async Task HandleTestAudioCommand()
        {
            SocketVoiceState? botVoiceState = (Context.Guild as SocketGuild)?.CurrentUser?.VoiceState;

            if (botVoiceState?.VoiceChannel == null)
            {
                await ReplyAsync("I am not currently in a voice channel.");
                return;
            }

            if (_audioService.IsPlaying)
            {
                await ReplyAsync("Audio is already playing.");
                return;
            }

            string filePath = System.IO.Path.Combine(AppContext.BaseDirectory, "test-audio.mp4");

            if (!System.IO.File.Exists(filePath))
            {
                await ReplyAsync($"Test audio file was not found at: {filePath}");
                return;
            }

            await ReplyAsync("Playing local test audio...");

            await _audioService.SendAudio(Context.Client.GetGuild(Context.Guild.Id).AudioClient, filePath);
        }
    }
}
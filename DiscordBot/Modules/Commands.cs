using Discord;
using Discord.Audio;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private static readonly ConcurrentDictionary<ulong, bool> RandomPlaylists = new ConcurrentDictionary<ulong, bool>();
        private static readonly ConcurrentDictionary<ulong, bool> LoopPlaylists = new ConcurrentDictionary<ulong, bool>();

        public Commands(AudioService audioService, PlaylistService playlistService)
        {
            _audioService = audioService;
            _playlistService = playlistService;

        }

        private async Task UpdateBotStatusAsync()
        {
            ulong guildId = Context.Guild.Id;

            bool loopEnabled = LoopPlaylists.ContainsKey(guildId);
            bool randomEnabled = RandomPlaylists.ContainsKey(guildId);

            string status = "!commands for help";

            if (loopEnabled)
                status += " | \U0001F501: ON";

            if (randomEnabled)
                status += " | \U0001F3B2: ON";

            await Context.Client.SetGameAsync(status);
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
            ulong guildId = Context.Guild.Id;

            if (!_audioService.IsPlaying)
            {
                await ReplyAsync("There is no song currently playing.");
                return;
            }

            _audioService.StopAudio();

            if (ActivePlaylists.ContainsKey(guildId))
            {
                await ReplyAsync("Skipped the current song in the music list.");
            }
            else
            {
                await ReplyAsync("Skipped the current song.");
            }
        }


        [Command("addurl", RunMode = RunMode.Async)]
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

        [Command("removeurl", RunMode = RunMode.Async)]
        public async Task HandleRemoveUrlSongCommand([Remainder] string url)
        {
            try
            {
                VideoId videoId = VideoId.Parse(url);

                var playlist = await _playlistService.GetPlaylistAsync(Context.Guild.Id);

                int songIndex = playlist.FindIndex(track => track.VideoId == videoId.ToString());

                if (songIndex == -1)
                {
                    await ReplyAsync("This song is not in the music list.");
                    return;
                }

                string removedSong = playlist[songIndex].Title;

                await _playlistService.RemoveTrackAsync(Context.Guild.Id, songIndex);

                await ReplyAsync($"Removed from music list: **{removedSong}**");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Remove URL song error: {ex}");

                await ReplyAsync("Could not remove the song. Please provide a valid YouTube URL.");
            }
        }

        [Command("add", RunMode = RunMode.Async)]
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

        [Command("remove", RunMode = RunMode.Async)]
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

        [Command("play", RunMode = RunMode.Async)]
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
                await ReplyAsync("The music list is already playing.");
                return;
            }

            try
            {
                List<PlaylistTrack> playlist = await _playlistService.GetPlaylistAsync(guildId);
                StopPlaylistRequests.TryRemove(guildId, out _);
                RandomPlaylists.TryRemove(guildId, out _);
                LoopPlaylists.TryRemove(guildId, out _);

                if (playlist.Count == 0)
                {
                    await ReplyAsync("Your music list is empty.");
                    return;
                }

                var youtube = new YoutubeClient();

                var random = new Random();

                HashSet<int> remainingIndices = new HashSet<int>(Enumerable.Range(0, playlist.Count));

                bool stoppedByRequest = false;

                await ReplyAsync($"Starting music list with {playlist.Count} songs.");

                while (true)
                {
                    if (StopPlaylistRequests.TryRemove(guildId, out _))
                    {
                        stoppedByRequest = true;
                        await ReplyAsync("Music list stopped.");
                        break;
                    }

                    if (remainingIndices.Count == 0)
                    {
                        if (!LoopPlaylists.ContainsKey(guildId))
                        {
                            break;
                        }

                        remainingIndices = new HashSet<int>(Enumerable.Range(0, playlist.Count));
                    }

                    int songIndex;

                    if (RandomPlaylists.ContainsKey(guildId))
                    {
                        int randomIndex = random.Next(remainingIndices.Count);
                        songIndex = remainingIndices.ElementAt(randomIndex);
                    }
                    else
                    {
                        songIndex = remainingIndices.Min();
                    }

                    remainingIndices.Remove(songIndex);

                    PlaylistTrack track = playlist[songIndex];

                    await ReplyAsync($"Now playing ({playlist.Count - remainingIndices.Count}/{playlist.Count}): **{track.Title}**\n{track.Url}");

                    string filePath = await DownloadPlaylistTrackAsync(track);

                    if (StopPlaylistRequests.TryRemove(guildId, out _))
                    {
                        stoppedByRequest = true;
                        await ReplyAsync("Music list stopped.");
                        break;
                    }

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

                if (!stoppedByRequest)
                {
                    await ReplyAsync("Music list finished!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Music list playback error: {ex}");
                await ReplyAsync("An error occurred while playing the music list.");
            }
            finally
            {
                ActivePlaylists.TryRemove(guildId, out _);
                RandomPlaylists.TryRemove(guildId, out _);
                LoopPlaylists.TryRemove(guildId, out _);
                StopPlaylistRequests.TryRemove(guildId, out _);
                CurrentTracks.TryRemove(guildId, out _);

                await Context.Client.SetGameAsync("!commands for help");
            }
        }

        [Command("stop", RunMode = RunMode.Async)]
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

        [Command("random", RunMode = RunMode.Async)]
        public async Task HandleRandomCommand()
        {
            ulong guildId = Context.Guild.Id;

            if (!ActivePlaylists.ContainsKey(guildId))
            {
                await ReplyAsync("There is no music list currently playing.");
                return;
            }

            if (!RandomPlaylists.TryAdd(guildId, true))
            {
                await ReplyAsync("Random playback is already enabled.");
                return;
            }

            await ReplyAsync("Random playback enabled.");
            await UpdateBotStatusAsync();
        }

        [Command("randomoff", RunMode = RunMode.Async)]
        public async Task HandleRandomOffCommand()
        {
            ulong guildId = Context.Guild.Id;

            if (!ActivePlaylists.ContainsKey(guildId))
            {
                await ReplyAsync("There is no music list currently playing.");
                return;
            }

            if (!RandomPlaylists.TryRemove(guildId, out _))
            {
                await ReplyAsync("Random playback is already disabled.");
                return;
            }

            await ReplyAsync("Random playback disabled.");
            await UpdateBotStatusAsync();
        }

        [Command("loop", RunMode = RunMode.Async)]
        public async Task HandleLoopCommand()
        {
            ulong guildId = Context.Guild.Id;

            if (!ActivePlaylists.ContainsKey(guildId))
            {
                await ReplyAsync("There is no music list currently playing.");
                return;
            }

            if (!LoopPlaylists.TryAdd(guildId, true))
            {
                await ReplyAsync("Music list looping is already enabled.");
                return;
            }

            await ReplyAsync("Music list looping enabled.");
            await UpdateBotStatusAsync();
        }

        [Command("loopoff", RunMode = RunMode.Async)]
        public async Task HandleLoopOffCommand()
        {
            ulong guildId = Context.Guild.Id;

            if (!ActivePlaylists.ContainsKey(guildId))
            {
                await ReplyAsync("There is no music list currently playing.");
                return;
            }

            if (!LoopPlaylists.TryRemove(guildId, out _))
            {
                await ReplyAsync("Music list looping is already disabled.");
                return;
            }

            await ReplyAsync("Music list looping disabled.");
            await UpdateBotStatusAsync();
        }

        [Command("list", RunMode = RunMode.Async)]
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
                "\U0001F3A4     !summon -> join your vc\n\n" +
                "\U0001F44B     !leave -> leave your vc\n\n" +
                "\U0001F3B5     !song + name of the song -> search and play the song from Youtube\n\n" +
                "\u23ED\uFE0F     !skip -> skip song or skip current song from music list\n\n" +
                "\U0001F3B6     !list -> shows all songs in music list\n\n" +
                "\u25B6\uFE0F     !play -> play saved music list\n\n" +
                "\u23F9\uFE0F     !stop -> stop the entire music list\n\n" +
                "\U0001F501     !loop -> enable music list looping\n\n" +
                "\U0001F501     !loopoff -> disable music list looping\n\n" +
                "\U0001F3B2     !random -> enable random song playback\n\n" +
                "\U0001F3B2     !randomoff -> disable random song playback\n\n" +
                "\u2795     !add -> add current song to music list\n\n" +
                "\u2796     !remove -> remove current song from music list\n\n" +
                "\u2795     !addurl + url of the song -> add that url song to the music list\n\n" +
                "\u2796     !removeurl + url of the song -> remove that url song from the music list"
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

            string filePath = System.IO.Path.Combine(AppContext.BaseDirectory, "mp4", "test-audio.mp4");

            if (!System.IO.File.Exists(filePath))
            {
                await ReplyAsync($"Test audio file was not found at: {filePath}");
                return;
            }

            await ReplyAsync("Playing local test audio...");

            await _audioService.SendAudio(Context.Client.GetGuild(Context.Guild.Id).AudioClient, filePath);
        }

        [Command("testtone", RunMode = RunMode.Async)]
        public async Task HandleTestToneCommand()
        {
            SocketVoiceState? botVoiceState = (Context.Guild as SocketGuild)?.CurrentUser?.VoiceState;

            if (botVoiceState?.VoiceChannel == null)
            {
                await ReplyAsync("I am not currently in a voice channel.");
                return;
            }

            if (_audioService.IsPlaying)
            {
                await ReplyAsync("Audio is already playing. Stop it before running this test.");
                return;
            }

            await ReplyAsync("Sending a 440 Hz test tone for 10 seconds...");

            IAudioClient audioClient = Context.Client.GetGuild(Context.Guild.Id).AudioClient;

            const int sampleRate = 48000;
            const int samplesPerFrame = 960;
            const int frameCount = 500;
            const double frequency = 440.0;
            const double amplitude = 5000.0;

            byte[] frame = new byte[samplesPerFrame * 2 * 2];

            using AudioOutStream discord = audioClient.CreatePCMStream(AudioApplication.Music);

            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                for (int i = 0; i < samplesPerFrame; i++)
                {
                    long sampleIndex = (long)frameIndex * samplesPerFrame + i;

                    short sample = (short)(Math.Sin(2.0 * Math.PI * frequency * sampleIndex / sampleRate) * amplitude);

                    int offset = i * 4;

                    BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(offset, 2), sample);
                    BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(offset + 2, 2), sample);
                }

                await discord.WriteAsync(frame, 0, frame.Length);

                long targetMilliseconds = (frameIndex + 1) * 20L;
                int remainingMilliseconds = (int)(targetMilliseconds - stopwatch.ElapsedMilliseconds);

                if (remainingMilliseconds > 0)
                {
                    await Task.Delay(remainingMilliseconds);
                }
            }

            await discord.FlushAsync();

            await ReplyAsync("Test tone finished.");
        }
    }
}
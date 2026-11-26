using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordBot.Modules
{
    public class PlaylistTrack
    {
        public string Title { get; set; } = "";
        public string VideoId { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public class PlaylistService
    {
        private readonly string _playlistDirectory = Path.Combine(AppContext.BaseDirectory, "Playlists");

        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new ConcurrentDictionary<ulong, SemaphoreSlim>();

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private string GetPlaylistPath(ulong guildId)
        {
            return Path.Combine(_playlistDirectory, $"{guildId}.json");
        }

        private SemaphoreSlim GetLock(ulong guildId)
        {
            return _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        }

        private async Task<List<PlaylistTrack>> LoadPlaylistInternalAsync(ulong guildId)
        {
            string path = GetPlaylistPath(guildId);

            if (!File.Exists(path)) return new List<PlaylistTrack>();

            string json = await File.ReadAllTextAsync(path);

            return JsonSerializer.Deserialize<List<PlaylistTrack>>(json) ?? new List<PlaylistTrack>();
        }

        private async Task SavePlaylistInternalAsync(ulong guildId, List<PlaylistTrack> playlist)
        {
            Directory.CreateDirectory(_playlistDirectory);

            string path = GetPlaylistPath(guildId);
            string tempPath = path + ".tmp";

            string json = JsonSerializer.Serialize(playlist, _jsonOptions);

            await File.WriteAllTextAsync(tempPath, json);

            File.Move(tempPath, path, true);
        }

        public async Task<List<PlaylistTrack>> GetPlaylistAsync(ulong guildId)
        {
            SemaphoreSlim semaphore = GetLock(guildId);

            await semaphore.WaitAsync();

            try
            {
                return await LoadPlaylistInternalAsync(guildId);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task AddTrackAsync(ulong guildId, PlaylistTrack track)
        {
            SemaphoreSlim semaphore = GetLock(guildId);

            await semaphore.WaitAsync();

            try
            {
                List<PlaylistTrack> playlist = await LoadPlaylistInternalAsync(guildId);

                playlist.Add(track);

                await SavePlaylistInternalAsync(guildId, playlist);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task RemoveTrackAsync(ulong guildId, int index)
        {
            SemaphoreSlim semaphore = GetLock(guildId);

            await semaphore.WaitAsync();

            try
            {
                List<PlaylistTrack> playlist = await LoadPlaylistInternalAsync(guildId);

                if (index < 0 || index >= playlist.Count) return;

                playlist.RemoveAt(index);

                await SavePlaylistInternalAsync(guildId, playlist);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}

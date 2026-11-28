using Discord.Audio;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DiscordBot.Modules
{
    public class AudioService
    {
        private Process ffmpeg;

        public bool IsPlaying { get; private set; }

        private static Process CreateStream(string path)
        {
            string ffmpegPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "ffmpeg"
                : Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg");

            return Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-threads 1 -i \"{path}\" -f s16le pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
        }

        public async Task SendAudio(IAudioClient audioClient, string path)
        {
            IsPlaying = true;

            using (ffmpeg = CreateStream(path))
            using (Stream output = ffmpeg.StandardOutput.BaseStream)
            using (AudioOutStream discord = audioClient.CreatePCMStream(AudioApplication.Music, bitrate: 64000, bufferMillis: 1000, packetLoss: 0))
            {
                try { await output.CopyToAsync(discord); }
                finally { await discord.FlushAsync(); }
            }

            IsPlaying = false;
        }

        public void StopAudio()
        {
            IsPlaying = false;
            ffmpeg.Kill();
            ffmpeg.Dispose();
        }
    }
}
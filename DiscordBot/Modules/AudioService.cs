
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

        public bool IsPlaying { get; internal set; }

        private static Process CreateStream(string path)
        {
            string ffmpegPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "ffmpeg"
                : Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg");

            return Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{path}\" -f s16le -ar 48000 -ac 2 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
        }

        public async Task SendAudio(IAudioClient audioClient, string path)
        {
            IsPlaying = true;

            try
            {
                using (ffmpeg = CreateStream(path))
                using (Stream output = ffmpeg.StandardOutput.BaseStream)
                using (AudioOutStream discord = audioClient.CreatePCMStream(AudioApplication.Music))
                {
                    try
                    {
                        await output.CopyToAsync(discord);
                    }
                    finally
                    {
                        await discord.FlushAsync();
                    }
                }
            }
            finally
            {
                IsPlaying = false;
                ffmpeg = null;
            }
        }

        public void StopAudio()
        {
            if (ffmpeg == null || ffmpeg.HasExited) return;

            ffmpeg.Kill();
        }
    }
}

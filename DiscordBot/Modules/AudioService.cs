
using Discord.Audio;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordBot.Modules
{
    public class AudioService
    {
        private Process ffmpeg;
        private CancellationTokenSource playbackCancellation;

        public bool IsPlaying { get; private set; }

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
            var cancellation = new CancellationTokenSource();

            playbackCancellation = cancellation;
            IsPlaying = true;

            try
            {
                using (ffmpeg = CreateStream(path))
                using (Stream output = ffmpeg.StandardOutput.BaseStream)
                using (AudioOutStream discord = audioClient.CreatePCMStream(AudioApplication.Music))
                {
                    try
                    {
                        await output.CopyToAsync(discord, 81920, cancellation.Token);
                    }
                    finally
                    {
                        if (cancellation.IsCancellationRequested && !ffmpeg.HasExited)
                        {
                            ffmpeg.Kill();
                        }

                        if (!cancellation.IsCancellationRequested)
                        {
                            await discord.FlushAsync();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Playback was cancelled.
            }
            finally
            {
                IsPlaying = false;
                ffmpeg = null;
                playbackCancellation = null;
                cancellation.Dispose();
            }
        }

        public void StopAudio()
        {
            playbackCancellation?.Cancel();

            if (ffmpeg != null && !ffmpeg.HasExited)
            {
                ffmpeg.Kill();
            }
        }
    }
}

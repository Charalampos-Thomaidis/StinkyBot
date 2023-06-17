using System.Diagnostics;
using System.Threading.Tasks;
using Discord.Audio;

namespace DiscordBot.Modules
{
    public class AudioService
    {
        private Process ffmpeg;

        public bool IsPlaying { get; internal set; }

        private Process CreateStream(string path)
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i {path} -f s16le -ar 48000 -ac 2 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            }); 
        }
        
        public async Task SendAudio(IAudioClient audioClient, string path)
        {
            IsPlaying = true;

            using (ffmpeg = CreateStream(path))
            using (var output = ffmpeg.StandardOutput.BaseStream)
            using (var discord = audioClient.CreatePCMStream(AudioApplication.Mixed))
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
using System.Threading.Tasks;
using Discord;
using NAudio.Wave;

namespace DiscordBot.Modules
{
    public class AudioService
    {
        private WaveOutEvent _waveOut;

        public async Task PlayAudio(IVoiceChannel voiceChannel, string filePath)
        {
            using (var audioFile = new AudioFileReader(filePath))
            using (var volumeAdjusted = new WaveChannel32(audioFile) { Volume = 0.2f })
            {
                _waveOut = new WaveOutEvent();
                _waveOut.Init(volumeAdjusted);
                _waveOut.Play();
                await Task.Delay(-1);
            }
        }

        public async Task StopAudio()
        {
            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }
            await Task.CompletedTask;
        }
    }
}
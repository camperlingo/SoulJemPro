using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SoulJemApp.Plugins
{
    public class FFmpegPlugin
    {
        public async Task<string> ProcessPitchAsync(string inputPath, int pitch, TimeSpan totalDuration, Action<int>? onProgress = null)
        {
            if (pitch == 0 || string.IsNullOrEmpty(inputPath)) return inputPath;

            string extension = Path.GetExtension(inputPath).ToLower();
            
            // 1. Definiamo la cartella Cache Temporanea (quella che si auto-pulisce ogni 8 ore)
            string cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SoulJem_v5", "Downloads");
            if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);

            // 2. Puliamo il nome del file originale per evitare la concatenazione "_pitch1_pitch2..."
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            if (baseName.Contains("_pitch")) 
            {
                baseName = baseName.Substring(0, baseName.IndexOf("_pitch"));
            }

            // 3. Creiamo il percorso finale ESCLUSIVAMENTE nella cartella temporanea
            string output = Path.Combine(cacheFolder, baseName + $"_pitch{pitch}{extension}");

            if (File.Exists(output))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[SMART CACHE] File già elaborato trovato in TEMP: {Path.GetFileName(output)}. Recupero istantaneo!");
                Console.ResetColor();
                
                onProgress?.Invoke(100); 
                return output; 
            }

            string pitchValue = Math.Pow(2, pitch / 12.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            
            string videoArg = (extension == ".mp4" || extension == ".mkv" || extension == ".avi") ? "-c:v copy" : "";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputPath}\" {videoArg} -af asetrate=44100*{pitchValue},aresample=44100 \"{output}\" -y",
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var regex = new Regex(@"time=(\d+:\d+:\d+\.\d+)", RegexOptions.Compiled);

            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;

                var match = regex.Match(line);
                if (match.Success && totalDuration.TotalSeconds > 0)
                {
                    if (TimeSpan.TryParse(match.Groups[1].Value, out var current))
                    {
                        double percent = (current.TotalSeconds / totalDuration.TotalSeconds) * 100;
                        if (percent > 100) percent = 100;
                        onProgress?.Invoke((int)percent);
                    }
                }
            }

            await process.WaitForExitAsync();
            return output;
        }

        public async Task<TimeSpan> GetDurationAsync(string inputPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (double.TryParse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture, out double seconds))
                return TimeSpan.FromSeconds(seconds);
            
            return TimeSpan.Zero;
        }
    }
}

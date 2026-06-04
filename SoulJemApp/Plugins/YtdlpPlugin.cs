using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic; 
using System.Text.Json;          
using SoulJemApp.Models;         

namespace SoulJemApp.Plugins
{
    public class YtdlpPlugin
    {
        private readonly string _downloadFolder;
        private string YtdlpPath = "yt-dlp"; 

        public YtdlpPlugin()
        {
            _downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SoulJem_v5", "Downloads");
            if (!Directory.Exists(_downloadFolder)) Directory.CreateDirectory(_downloadFolder);
        }

        public async Task UpdateYtdlpAsync()
        {
            var p = Process.Start(new ProcessStartInfo { FileName = "yt-dlp", Arguments = "-U", UseShellExecute = false, CreateNoWindow = true });
            if (p != null) await p.WaitForExitAsync();
        }

        // ORA RESTITUISCE IL TUO MODELLO ORIGINALE COMPLETO DI IMMAGINE!
        public async Task<List<YouTubeResultItem>> SearchAsync(string query, int maxResults = 9)
        {
            var results = new List<YouTubeResultItem>();
            try
            {
                var startInfo = new ProcessStartInfo {
                    FileName = YtdlpPath,
                    Arguments = $"\"ytsearch{maxResults}:{query}\" --dump-json --no-playlist",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return results;

                using var reader = process.StandardOutput;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        results.Add(new YouTubeResultItem {
                            Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "N/A" : "N/A",
                            Url = root.TryGetProperty("webpage_url", out var u) ? u.GetString() ?? "" : "",
                            Duration = root.TryGetProperty("duration_string", out var d) ? d.GetString() ?? "N/A" : "N/A",
                            // MAGIA: ORA ESTRAE ANCHE IL LINK DELLA COPERTINA!
                            ThumbnailUrl = root.TryGetProperty("thumbnail", out var th) ? th.GetString() ?? "" : ""
                        });
                    } catch { }
                }
                await process.WaitForExitAsync();
            } catch { }
            return results;
        }

        public async Task<string> GetTitleFromUrlAsync(string url)
        {
            try {
                var startInfo = new ProcessStartInfo { FileName = YtdlpPath, Arguments = $"--dump-json --no-playlist \"{url}\"", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var process = Process.Start(startInfo);
                if (process == null) return "";
                string? line = await process.StandardOutput.ReadLineAsync();
                await process.WaitForExitAsync();
                if (!string.IsNullOrEmpty(line)) {
                    using var doc = JsonDocument.Parse(line);
                    return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                }
            } catch { }
            return "";
        }

        public async Task<string> DownloadOrSearchAsync(string query, string targetFolder, Action<int>? onProgress = null, bool onlyAudio = false)
        {
            string folder = string.IsNullOrEmpty(targetFolder) ? _downloadFolder : targetFolder;
            string outputTemplate = Path.Combine(folder, "%(title)s.%(ext)s");
            
            // AGGIUNTI I PARAMETRI ANTI-BOT QUI SOTTO
            string antiBotParams = "--extractor-args \"youtube:player_client=android,web\" --geo-bypass --user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36\"";

            string format = onlyAudio ? 
                $"-f \"bestaudio\" --extract-audio --audio-format mp3 --audio-quality 0 {antiBotParams}" : 
                $"-f \"bestvideo[ext=mp4][vcodec^=avc1][height<=720]+bestaudio[ext=m4a]/best[ext=mp4]/best\" --merge-output-format mp4 {antiBotParams}";
            
            return await ExecuteDownloadProcess(folder, $"--newline --color never --no-playlist {format} -o \"{outputTemplate}\" \"{query}\"", onlyAudio ? "*.mp3" : "*.mp4", onProgress);
        }

        public async Task<string> DownloadAudioCustomAsync(string url, string targetFolder, string fileName, string format, string bitrate, string hz, Action<int>? onProgress = null)
        {
            string outputTemplate = Path.Combine(targetFolder, $"{fileName}.%(ext)s");
            string qualityVal = bitrate.Contains("VBR") ? "0" : bitrate;
            
            // AGGIUNTI I PARAMETRI ANTI-BOT ANCHE QUI PER I DOWNLOAD CUSTOM
            string antiBotParams = "--extractor-args \"youtube:player_client=android,web\" --geo-bypass --user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36\"";
            
            string args = $"--newline --color never --no-playlist -f \"bestaudio/best\" --extract-audio --audio-format {format} --audio-quality {qualityVal} --postprocessor-args \"-ar {hz}\" {antiBotParams} -o \"{outputTemplate}\" \"{url}\"";

            return await ExecuteDownloadProcess(targetFolder, args, $"*.{format}", onProgress);
        }

        private async Task<string> ExecuteDownloadProcess(string folder, string args, string extFilter, Action<int>? onProgress)
        {
            var psi = new ProcessStartInfo { 
                FileName = YtdlpPath, 
                Arguments = args, 
                RedirectStandardOutput = true, 
                RedirectStandardError = true,
                UseShellExecute = false, 
                CreateNoWindow = true 
            };
            
            using var process = new Process { StartInfo = psi };
            process.Start();

            var regex = new Regex(@"(\d+(\.\d+)?)%", RegexOptions.Compiled);

            var outTask = Task.Run(async () => {
                while (!process.StandardOutput.EndOfStream) {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    
                    Console.WriteLine($"[YT-DLP] {line}");

                    if (line.Contains("already been downloaded") || line.Contains("ExtractAudio")) 
                        onProgress?.Invoke(100);

                    var match = regex.Match(line);
                    if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var percent))
                        onProgress?.Invoke((int)percent);
                }
            });

            var errTask = Task.Run(async () => {
                while (!process.StandardError.EndOfStream) {
                    var line = await process.StandardError.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    Console.WriteLine($"[YT-DLP INFO/ERR] {line}");
                }
            });

            await Task.WhenAll(outTask, errTask);
            await process.WaitForExitAsync();

            var directory = new DirectoryInfo(folder);
            var myFile = directory.GetFiles(extFilter).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            return myFile?.FullName ?? "";
        }
    }
}

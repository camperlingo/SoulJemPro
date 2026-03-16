using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace SoulJemApp.Plugins
{
    public class MpvPlugin
    {
        private Process? _previewProcess;
        private Process? _salaProcess;
        private Process? _radioProcess;

        public bool IsSalaOn { get; private set; } = false;
        
        // --- MEMORIA PER LA RESURREZIONE ---
        private string _lastFilePath = "";
        private bool _lastIsLoop = false;
        private double _lastPreviewTime = 0;
        // -----------------------------------

        public Action<int>? OnRadioProgressChanged;
        public Action<int>? OnPreviewProgressChanged; 
        public Action<double>? OnPreviewTimeChanged; 
        public Action<double>? OnSalaTimeChanged; 
        public event Action? OnPreviewTrackFinished; 
        public event EventHandler? OnPreviewEnded; 

        private CancellationTokenSource _ipcListenerCts = new CancellationTokenSource();

        public MpvPlugin()
        {
            StartIpcListener("/tmp/souljem_preview", percent => OnPreviewProgressChanged?.Invoke(percent), true, _ipcListenerCts.Token);
            StartIpcListener("/tmp/souljem_radio", percent => OnRadioProgressChanged?.Invoke(percent), false, _ipcListenerCts.Token);
        }

        private void StartIpcListener(string socketPath, Action<int> onProgress, bool isPreview, CancellationToken token)
        {
            Task.Run(async () =>
            {
                var encoding = new UTF8Encoding(false); 
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!File.Exists(socketPath)) { await Task.Delay(500, token); continue; }
                        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                        using var stream = new NetworkStream(socket);
                        using var writer = new StreamWriter(stream, encoding) { AutoFlush = true };
                        using var reader = new StreamReader(stream, encoding);
                        
                        await writer.WriteLineAsync("{\"command\": [\"observe_property\", 1, \"percent-pos\"]}");
                        if (isPreview) await writer.WriteLineAsync("{\"command\": [\"observe_property\", 2, \"eof-reached\"]}");
                        await writer.WriteLineAsync("{\"command\": [\"observe_property\", 3, \"time-pos\"]}");

                        while (!token.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync();
                            if (line == null) break; 
                            if (string.IsNullOrEmpty(line)) continue;
                            
                            try {
                                using var doc = JsonDocument.Parse(line);
                                if (doc.RootElement.TryGetProperty("name", out var nameProp))
                                {
                                    if (nameProp.GetString() == "percent-pos")
                                    {
                                        if (doc.RootElement.TryGetProperty("data", out var data)) onProgress((int)data.GetDouble());
                                    }
                                    else if (nameProp.GetString() == "time-pos")
                                    {
                                        if (doc.RootElement.TryGetProperty("data", out var data))
                                        {
                                            double timePos = data.GetDouble();
                                            if (isPreview) 
                                            {
                                                _lastPreviewTime = timePos; // Salviamo il tempo corrente!
                                                OnPreviewTimeChanged?.Invoke(timePos);
                                            }
                                            else OnSalaTimeChanged?.Invoke(timePos);
                                        }
                                    }
                                }
                                
                                if (isPreview && doc.RootElement.TryGetProperty("event", out var evProp) && evProp.GetString() == "property-change")
                                {
                                    if (doc.RootElement.TryGetProperty("name", out var evName) && evName.GetString() == "eof-reached")
                                    {
                                        if (doc.RootElement.TryGetProperty("data", out var eofData) && eofData.GetBoolean() == true) OnPreviewTrackFinished?.Invoke();
                                    }
                                }
                            } catch { }
                        }
                    } catch { await Task.Delay(1000, token); }
                }
            }, token);
        }

        private void SendIpcCommand(string socketPath, string commandJson)
        {
            if (!File.Exists(socketPath)) return; 

            Task.Run(async () => 
            {
                try
                {
                    using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    using var cts = new CancellationTokenSource(100); 
                    await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cts.Token);
                    
                    using var stream = new NetworkStream(client);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    await writer.WriteLineAsync(commandJson);
                }
                catch { }
            });
        }

        private int GetActiveMonitorsCount()
        {
            try {
                var p = new Process { 
                    StartInfo = new ProcessStartInfo { 
                        FileName = "xrandr", 
                        Arguments = "--listactivemonitors", 
                        RedirectStandardOutput = true, 
                        UseShellExecute = false,
                        CreateNoWindow = true
                    } 
                };
                p.Start();
                string outStr = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                
                var lines = outStr.Split('\n');
                if (lines.Length > 0 && lines[0].Trim().StartsWith("Monitors:")) {
                    string countStr = lines[0].Replace("Monitors:", "").Trim();
                    if (int.TryParse(countStr, out int num)) return num;
                }
            } catch { }
            return 1;
        }

        public void LaunchPreview(string filePath, string xidString, int initialVolume = 100, bool isLoop = false)
        {
            StopPreview();
            StopSala(); 

            // Salviamo i dati per un'eventuale resurrezione!
            _lastFilePath = filePath;
            _lastIsLoop = isLoop;

            string target = string.IsNullOrEmpty(filePath) ? "av://lavfi:color=c=blue:s=1280x720" : $"\"{filePath}\"";
            
            // PREVIEW
            string prevArgs = $"--pause --wid={xidString} --no-border --force-window=yes --keep-open=yes --cursor-autohide=1000 --osd-level=0 --osc=no --input-vo-keyboard=no --image-display-duration=inf --vo=gpu --hwdec=vaapi --profile=fast --ytdl-format=\"bestvideo[ext=mp4][vcodec*=avc][height<=720]+bestaudio/best\"";
            if (isLoop) prevArgs += " --loop-file=inf --vid=1 --aid=no"; 
            else prevArgs += $" --volume={initialVolume}";
            
            _previewProcess = StartMpvProcess(target, "SoulJem_Preview", prevArgs, true, "/tmp/souljem_preview", "SoulJemBase");

            // SALA
            string salaSocket = "/tmp/souljem_sala";
            string salaArgs = $"--pause --force-window=yes --keep-open=yes --osd-level=0 --osc=no --image-display-duration=inf --ao=null --input-ipc-server={salaSocket} --vo=gpu --hwdec=vaapi --profile=fast --video-sync=audio --hr-seek-framedrop=yes --ytdl-format=\"bestvideo[ext=mp4][vcodec*=avc][height<=720]+bestaudio/best\"";
            
            if (GetActiveMonitorsCount() > 1) {
                salaArgs += " --screen=1 --fs-screen=1";
            }

            if (isLoop) salaArgs += " --loop-file=inf --vid=1";
            salaArgs += " --window-minimized=yes";
            
            _salaProcess = StartMpvProcess(target, "SoulJem_Sala", salaArgs, false, "", "SoulJemSala", true);

            Task.Run(async () => {
                int tentativi = 0;
                while (tentativi < 20) { 
                    await Task.Delay(500); 
                    if (IsSalaOn || _salaProcess == null || _salaProcess.HasExited) break;
                    RunWmctrl("-r \"SoulJem_Sala\" -b add,skip_taskbar,skip_pager");
                    tentativi++;
                    SendIpcCommand("/tmp/souljem_sala", "{\"command\": [\"set_property\", \"window-minimized\", true]}");
                }
            });
        }

        public void ToggleSala(bool turnOn, int screenCount)
        {
            IsSalaOn = turnOn;
            string salaSocket = "/tmp/souljem_sala";
            
            if (turnOn)
            {
                // --- PROTOCOLLO DI RESURREZIONE ---
                if (_salaProcess == null || _salaProcess.HasExited)
                {
                    Console.WriteLine("[SISTEMA] Finestra Sala chiusa accidentalmente. Riavvio in corso...");
                    string target = string.IsNullOrEmpty(_lastFilePath) ? "av://lavfi:color=c=blue:s=1280x720" : $"\"{_lastFilePath}\"";
                    string salaArgs = $"--pause --force-window=yes --keep-open=yes --osd-level=0 --osc=no --image-display-duration=inf --ao=null --input-ipc-server={salaSocket} --vo=gpu --hwdec=vaapi --profile=fast --video-sync=audio --hr-seek-framedrop=yes --ytdl-format=\"bestvideo[ext=mp4][vcodec*=avc][height<=720]+bestaudio/best\"";
                    
                    if (GetActiveMonitorsCount() > 1) {
                        salaArgs += " --screen=1 --fs-screen=1";
                    }
                    if (_lastIsLoop) salaArgs += " --loop-file=inf --vid=1";
                    
                    _salaProcess = StartMpvProcess(target, "SoulJem_Sala", salaArgs, false, "", "SoulJemSala", true);
                    
                    Task.Run(async () => {
                        await Task.Delay(1000); // Diamo 1 secondo a MPV per aprire i tubi
                        SyncSalaToTime(_lastPreviewTime); // Saltiamo al punto esatto del brano!
                        SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"pause\", false]}"); // Togliamo la pausa
                        
                        if (screenCount > 1) {
                            SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"fullscreen\", true]}");
                            SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"ontop\", true]}");
                        } else {
                            SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"geometry\", \"640x360\"]}");
                        }
                        RunWmctrl("-a \"SoulJem_Sala\"");
                    });
                    
                    return; // Usciamo, il processo è rinato!
                }
                // ----------------------------------

                // Comportamento NORMALE se la finestra è viva:
                RunWmctrl("-r \"SoulJem_Sala\" -b remove,skip_taskbar,skip_pager");
                SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"window-minimized\", false]}");
                
                if (screenCount > 1)
                {
                    SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"fullscreen\", true]}");
                    SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"ontop\", true]}");
                }
                else
                {
                    SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"ontop\", false]}");
                    SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"fs\", false]}");
                    SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"geometry\", \"640x360\"]}");
                }
                RunWmctrl("-a \"SoulJem_Sala\"");
            }
            else
            {
                if (_salaProcess != null && !_salaProcess.HasExited)
                {
                    SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"window-minimized\", true]}");
                    SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"fullscreen\", false]}");
                    SendIpcCommand(salaSocket, "{\"command\": [\"set_property\", \"ontop\", false]}");
                    
                    Task.Run(async () => {
                        await Task.Delay(200);
                        RunWmctrl("-r \"SoulJem_Sala\" -b add,skip_taskbar,skip_pager");
                    });
                }
            }
        }

        private void RunWmctrl(string args)
        {
            try { Process.Start(new ProcessStartInfo { FileName = "wmctrl", Arguments = args, UseShellExecute = false, CreateNoWindow = true }); } catch { }
        }

        public void StopPreview() { KillProcess(ref _previewProcess); }
        public void StopSala() => KillProcess(ref _salaProcess);

        public void PlayRadio(string radioUrl, int initialVolume = 100)
        {
            StopRadio();
            _radioProcess = StartMpvProcess($"\"{radioUrl}\"", "SoulJem Radio", $"--vid=no --loop-file=inf --volume={initialVolume}", false, "/tmp/souljem_radio", "SoulJemRadio");
        }
        
        public void PlayRadioFileWithFolder(string filePath, int initialVolume = 100)
        {
            StopRadio();
            string dir = Path.GetDirectoryName(filePath) ?? "";
            string target = $"\"{filePath}\" \"{dir}\"";
            _radioProcess = StartMpvProcess(target, "SoulJem Radio", $"--vid=no --loop-playlist=inf --volume={initialVolume}", false, "/tmp/souljem_radio", "SoulJemRadio");
            Task.Run(async () => {
                await Task.Delay(1500);
                SendIpcCommand("/tmp/souljem_radio", "{\"command\": [\"playlist-shuffle\"]}");
            });
        }

        public void PlayRadioDirectory(string path, int initialVolume = 100)
        {
            StopRadio();
            _radioProcess = StartMpvProcess($"\"{path}\"", "SoulJem Radio", $"--vid=no --loop-playlist=inf --shuffle --volume={initialVolume}", false, "/tmp/souljem_radio", "SoulJemRadio");
        }
        public void StopRadio() => KillProcess(ref _radioProcess);

        public void SetPreviewVolume(int volume) => SendIpcCommand("/tmp/souljem_preview", $"{{\"command\": [\"set_property\", \"volume\", {volume}]}}");
        public void SetRadioVolume(int volume) => SendIpcCommand("/tmp/souljem_radio", $"{{\"command\": [\"set_property\", \"volume\", {volume}]}}");
        
        public void SeekPreview(int percent)
        {
            SendIpcCommand("/tmp/souljem_preview", $"{{\"command\": [\"seek\", {percent}, \"absolute-percent\"]}}");
            SendIpcCommand("/tmp/souljem_sala", $"{{\"command\": [\"seek\", {percent}, \"absolute-percent\"]}}"); 
        }
        
        public void SeekSala(int percent) => SendIpcCommand("/tmp/souljem_sala", $"{{\"command\": [\"seek\", {percent}, \"absolute-percent\"]}}");
        public void SyncSalaToTime(double timeInSec) => SendIpcCommand("/tmp/souljem_sala", $"{{\"command\": [\"set_property\", \"time-pos\", {timeInSec.ToString(System.Globalization.CultureInfo.InvariantCulture)}]}}");
        public void SyncPreviewToTime(double timeInSec) => SendIpcCommand("/tmp/souljem_preview", $"{{\"command\": [\"set_property\", \"time-pos\", {timeInSec.ToString(System.Globalization.CultureInfo.InvariantCulture)}]}}");
        
        public void SeekRadio(int percent) => SendIpcCommand("/tmp/souljem_radio", $"{{\"command\": [\"seek\", {percent}, \"absolute-percent\"]}}");
        
        public void RadioNext() => SendIpcCommand("/tmp/souljem_radio", "{\"command\": [\"playlist-next\"]}");
        public void RadioPrev() => SendIpcCommand("/tmp/souljem_radio", "{\"command\": [\"playlist-prev\"]}");

        public void TogglePausePreview(bool isPaused)
        {
            string pauseStr = isPaused ? "true" : "false";
            SendIpcCommand("/tmp/souljem_preview", $"{{\"command\": [\"set_property\", \"pause\", {pauseStr}]}}");
            SendIpcCommand("/tmp/souljem_sala", $"{{\"command\": [\"set_property\", \"pause\", {pauseStr}]}}"); 
            
            if (!isPaused)
            {
                Task.Run(async () => {
                    await Task.Delay(100);
                    SendIpcCommand("/tmp/souljem_sala", $"{{\"command\": [\"seek\", \"+0\", \"relative+exact\"]}}");
                });
            }
        }

        public void TogglePauseRadio(bool pause) => SendIpcCommand("/tmp/souljem_radio", $"{{\"command\": [\"set_property\", \"pause\", {(pause ? "true" : "false")}]}}");

        public void SetLivePitch(double pitchFactor)
        {
            string commandJson = $"{{\"command\": [\"set_property\", \"af\", \"rubberband=pitch-scale={pitchFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"]}}";
            SendIpcCommand("/tmp/souljem_preview", commandJson);
        }

        private Process StartMpvProcess(string target, string title, string extraArgs, bool isPreview = false, string ipcSocket = "", string audioClientName = "", bool isMuted = false)
        {
            if (!isMuted) extraArgs += " --ao=pulse --volume-max=200";
            
            if (!string.IsNullOrEmpty(ipcSocket)) { if (File.Exists(ipcSocket)) File.Delete(ipcSocket); extraArgs += $" --input-ipc-server={ipcSocket}"; }
            if (!string.IsNullOrEmpty(audioClientName)) extraArgs += $" --audio-client-name={audioClientName}"; 

            var p = new Process();
            p.StartInfo.FileName = "mpv";
            p.StartInfo.Arguments = $"{target} --title=\"{title}\" {extraArgs}";
            p.StartInfo.UseShellExecute = false; 
            p.StartInfo.CreateNoWindow = true;

            if (isPreview)
            {
                p.EnableRaisingEvents = true;
                p.Exited += (s, e) => { OnPreviewEnded?.Invoke(this, EventArgs.Empty); };
            }
            p.Start(); return p;
        }

        private void KillProcess(ref Process? p) { if (p != null && !p.HasExited) { try { p.Kill(); } catch { } p.Dispose(); } p = null; }
        ~MpvPlugin() { _ipcListenerCts.Cancel(); }
    }
}

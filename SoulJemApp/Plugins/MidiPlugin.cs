using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace SoulJemApp.Plugins
{
    // Struttura che memorizza il millisecondo esatto di ogni sillaba!
    public class MidiLyric
    {
        public double TimeSec { get; set; }
        public string Text { get; set; } = "";
    }

    // --- LA MAGIA: L'Array Intelligente per i Canali Muti ---
    public class ChannelMuteCollection
    {
        private bool[] _mutes = new bool[16];
        private MidiPlugin _plugin;

        public ChannelMuteCollection(MidiPlugin plugin) { _plugin = plugin; }

        public bool this[int index]
        {
            get => _mutes[index];
            set
            {
                _mutes[index] = value;
                // Se mettiamo in Muto (true), spara subito il comando "ZITTI TUTTI" al sintetizzatore!
                if (value) 
                {
                    _plugin.SilenceChannel(index);
                }
            }
        }
    }

    public class MidiPlugin
    {
        private Process? _fluidSynthProcess;
        private StreamWriter? _synthInput;
        private CancellationTokenSource? _cancellationTokenSource;

        public bool IsPlaying { get; private set; } = false;
        public bool IsPaused { get; set; } = false;
        public double TempoScale { get; set; } = 1.0;
        
        // Usiamo la nostra nuova collezione intelligente invece di un array stupido
        public ChannelMuteCollection ChannelMutes { get; private set; }
        
        public double CurrentTime { get; private set; } = 0.0;
        public double TotalTime { get; private set; } = 0.0;
        public string CurrentMidiFile { get; private set; } = "";
        
        public string[] ChannelInstrumentNames { get; set; } = new string[16];
        public List<MidiLyric> Lyrics { get; private set; } = new List<MidiLyric>();

        public Action<double>? OnProgressChanged;
        public Action? OnTrackFinished;

        private int _pitchShift = 0;
        public int PitchShift
        {
            get => _pitchShift;
            set { _pitchShift = value; FlushNotes(); } 
        }

        private int _masterVolume = 40;
        public int MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = value;
                if (_synthInput != null && _fluidSynthProcess != null && !_fluidSynthProcess.HasExited)
                {
                    try {
                        double gain = (value / 100.0) * 1.5;
                        _synthInput.WriteLine($"gain {gain.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                    } catch { }
                }
            }
        }

        private readonly string[] GmInstruments = {
            "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano", "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavi",
            "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
            "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
            "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)", "Electric Guitar (clean)", "Electric Guitar (muted)", "Overdriven Guitar", "Distortion Guitar", "Guitar harmonics",
            "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
            "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
            "String Ensemble 1", "String Ensemble 2", "SynthStrings 1", "SynthStrings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
            "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "SynthBrass 1", "SynthBrass 2",
            "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
            "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
            "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)", "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)",
            "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)", "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
            "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)", "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
            "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bag pipe", "Fiddle", "Shanai",
            "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
            "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot"
        };

        public MidiPlugin()
        {
            // Inizializza l'array intelligente!
            ChannelMutes = new ChannelMuteCollection(this);
            for (int i = 0; i < 16; i++) 
            {
                ChannelMutes[i] = false;
                ChannelInstrumentNames[i] = (i == 9) ? "Drum Kit" : "Acoustic Grand Piano";
            }
        }

        // --- IL SILENZIATORE SPECIFICO PER CANALE ---
        public void SilenceChannel(int channel)
        {
            if (_synthInput == null || _fluidSynthProcess == null || _fluidSynthProcess.HasExited) return;
            try {
                _synthInput.WriteLine($"cc {channel} 123 0"); // All Notes Off (Spegni le note incantate)
                _synthInput.WriteLine($"cc {channel} 120 0"); // All Sound Off (Stronca eventuali echi)
                _synthInput.WriteLine($"cc {channel} 64 0");  // Sustain Off (Rilascia il pedale)
            } catch { }
        }

        private void ExtractData(string filePath)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[MIDI ENGINE] Scansione Strumenti e Testo in corso...");
            Console.ResetColor();

            for (int i = 0; i < 16; i++) ChannelInstrumentNames[i] = (i == 9) ? "Drum Kit" : "Acoustic Grand Piano";
            Lyrics.Clear();

            try
            {
                var midiFile = MidiFile.Read(filePath);
                var tempoMap = midiFile.GetTempoMap();
                var allEvents = midiFile.GetTrackChunks().SelectMany(c => c.Events);
                
                // Estrazione Strumenti
                foreach (var ev in allEvents)
                {
                    if (ev is ProgramChangeEvent pc && pc.Channel != 9)
                        ChannelInstrumentNames[pc.Channel] = GmInstruments[pc.ProgramNumber];
                }

                // Estrazione Testo (Sillabe Karaoke)
                var timedEvents = midiFile.GetTimedEvents();
                foreach (var te in timedEvents)
                {
                    if (te.Event is LyricEvent le)
                    {
                        double t = te.TimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1000000.0;
                        Lyrics.Add(new MidiLyric { TimeSec = t, Text = le.Text });
                    }
                    else if (te.Event is TextEvent txtEv) 
                    {
                        // File .KAR vecchi usano i TextEvent al posto dei LyricEvent
                        string txt = txtEv.Text.Trim();
                        if (txt.StartsWith("@") || txt.StartsWith("%")) continue; // Ignora metadati strani
                        double t = te.TimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1000000.0;
                        Lyrics.Add(new MidiLyric { TimeSec = t, Text = txtEv.Text });
                    }
                }
                
                Lyrics = Lyrics.OrderBy(l => l.TimeSec).ToList();
                Console.WriteLine($"[MIDI ENGINE] Trovate {Lyrics.Count} sillabe per il Karaoke!");
            }
            catch { Console.WriteLine("[MIDI ENGINE] Errore durante l'estrazione dei dati."); }
        }

        public void PlayMidi(string filePath)
        {
            CurrentMidiFile = filePath;
            ExtractData(filePath); 
            
            StopPlayback();
            StartEngine();

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            Task.Run(() => PlaybackLoop(filePath, token), token);
        }

        public void StartEngine()
        {
            if (_fluidSynthProcess != null && !_fluidSynthProcess.HasExited) return;

            string sf2Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SoulJem_v5", "FluidR3_GM.sf2");
            
            if (!File.Exists(sf2Path)) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[MIDI ENGINE] ATTENZIONE: Banco suoni non trovato in {sf2Path}!");
                Console.ResetColor();
            }

            _fluidSynthProcess = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "fluidsynth",
                    Arguments = $"-a pulseaudio -m alsa_seq \"{sf2Path}\"", 
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            try {
                _fluidSynthProcess.Start();
                _synthInput = _fluidSynthProcess.StandardInput;
                _synthInput.AutoFlush = true; 
                MasterVolume = _masterVolume; 
                Console.WriteLine($"[MIDI ENGINE] Sintetizzatore FluidSynth avviato con {Path.GetFileName(sf2Path)}.");
            } catch (Exception ex) {
                Console.WriteLine($"[MIDI ENGINE ERRORE] Impossibile avviare FluidSynth: {ex.Message}");
            }
        }

        public void StopEngine()
        {
            StopPlayback();
            if (_fluidSynthProcess != null && !_fluidSynthProcess.HasExited) {
                try { 
                    _synthInput?.WriteLine("quit"); 
                    _fluidSynthProcess.Kill();
                } catch { }
            }
        }

        public void StopPlayback()
        {
            _cancellationTokenSource?.Cancel();
            IsPlaying = false;
            IsPaused = false;
            FlushNotes(); 
        }

        public void FlushNotes()
        {
            for (int i = 0; i < 16; i++) {
                SilenceChannel(i);
            }
        }

        private void PlaybackLoop(string filePath, CancellationToken token)
        {
            try
            {
                IsPlaying = true;
                CurrentTime = 0;
                
                var midiFile = MidiFile.Read(filePath);
                var tempoMap = midiFile.GetTempoMap();
                var timedEvents = midiFile.GetTimedEvents().ToList();
                
                if (timedEvents.Any()) {
                    var lastEventTime = timedEvents.Last().TimeAs<MetricTimeSpan>(tempoMap);
                    TotalTime = lastEventTime.TotalMicroseconds / 1000000.0;
                }

                var stopwatch = Stopwatch.StartNew();
                double lastEventTimeSec = 0;

                foreach (var te in timedEvents)
                {
                    if (token.IsCancellationRequested) break;

                    while (IsPaused) {
                        if (token.IsCancellationRequested) break;
                        Thread.Sleep(50);
                        stopwatch.Restart(); 
                    }

                    var metricTime = te.TimeAs<MetricTimeSpan>(tempoMap);
                    double eventTimeSec = metricTime.TotalMicroseconds / 1000000.0;

                    double delaySec = (eventTimeSec - lastEventTimeSec) / Math.Max(0.1, TempoScale);

                    if (delaySec > 0) {
                        long targetTicks = stopwatch.ElapsedTicks + (long)(delaySec * Stopwatch.Frequency);
                        while (stopwatch.ElapsedTicks < targetTicks) {
                            if (token.IsCancellationRequested) break;
                            Thread.SpinWait(10); 
                        }
                    }

                    lastEventTimeSec = eventTimeSec;
                    CurrentTime = eventTimeSec;

                    ProcessMidiEvent(te.Event);
                    
                    if (stopwatch.ElapsedMilliseconds > 200) {
                        double progress = (CurrentTime / TotalTime) * 100;
                        OnProgressChanged?.Invoke(progress);
                        stopwatch.Restart();
                    }
                }

                if (!token.IsCancellationRequested) {
                    IsPlaying = false;
                    OnTrackFinished?.Invoke();
                }
            }
            catch (Exception ex) { Console.WriteLine($"[MIDI ENGINE ERRORE] Impossibile leggere il file: {ex.Message}"); }
        }

        private void ProcessMidiEvent(MidiEvent midiEvent)
        {
            if (_synthInput == null || _fluidSynthProcess == null || _fluidSynthProcess.HasExited) return;
            try {
                if (midiEvent is ChannelEvent channelEvent) {
                    int channel = channelEvent.Channel;
                    if (ChannelMutes[channel]) return;

                    if (channelEvent is NoteOnEvent noteOn) {
                        int note = ApplyPitchShift(noteOn.NoteNumber, channel);
                        _synthInput.WriteLine($"noteon {channel} {note} {noteOn.Velocity}");
                    }
                    else if (channelEvent is NoteOffEvent noteOff) {
                        int note = ApplyPitchShift(noteOff.NoteNumber, channel);
                        _synthInput.WriteLine($"noteoff {channel} {note}");
                    }
                    else if (channelEvent is ProgramChangeEvent progChange) {
                        _synthInput.WriteLine($"prog {channel} {progChange.ProgramNumber}");
                    }
                    else if (channelEvent is ControlChangeEvent controlChange) {
                        _synthInput.WriteLine($"cc {channel} {controlChange.ControlNumber} {controlChange.ControlValue}");
                    }
                    else if (channelEvent is PitchBendEvent pitchBend) {
                        _synthInput.WriteLine($"pitch_bend {channel} {pitchBend.PitchValue}");
                    }
                }
            } catch { }
        }

        private int ApplyPitchShift(int note, int channel)
        {
            if (channel == 9) return note; // Non alterare la batteria!
            int shifted = note + PitchShift;
            return Math.Clamp(shifted, 0, 127);
        }
    }
}

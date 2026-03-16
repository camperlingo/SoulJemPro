using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SoulJemApp.Plugins;
using System;
using System.Collections.Generic;

namespace SoulJemApp.Views
{
    public class KaraokeLine
    {
        public double StartTime { get; set; }
        public List<MidiLyric> Syllables { get; set; } = new List<MidiLyric>();
    }

    public partial class TestoWindow : UserControl
    {
        private MidiPlugin _midiEngine = null!;
        private DispatcherTimer _renderTimer = null!;
        private List<KaraokeLine> _karaokeLines = new List<KaraokeLine>();
        private double _currentScrollY = 100; 

        // Cache dei colori per avere zero lag e fluidità massima!
        private static readonly IBrush ColorPast = SolidColorBrush.Parse("#444444");
        private static readonly IBrush ColorFuture = SolidColorBrush.Parse("#DDDDDD");
        private static readonly IBrush ColorCurrent = SolidColorBrush.Parse("#FFD700");

        public TestoWindow() { InitializeComponent(); }

        public TestoWindow(MidiPlugin engine) : this()
        {
            _midiEngine = engine;
            BuildLinesFromLyrics();
            DrawAllLinesToScreen();

            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _renderTimer.Tick += OnRenderTick;
            _renderTimer.Start();
        }

        private void BuildLinesFromLyrics()
        {
            KaraokeLine currentLine = new KaraokeLine();
            foreach (var lyric in _midiEngine.Lyrics)
            {
                string txt = lyric.Text;
                
                // Rileva solo gli a capo veri del file MIDI per evitare di mozzare le frasi!
                bool isNewLine = txt.StartsWith("/") || txt.StartsWith("\\") || txt.StartsWith("\r") || txt.StartsWith("\n");
                txt = txt.Replace("/", "").Replace("\\", "").Replace("\r", "").Replace("\n", "");
                
                if (isNewLine && currentLine.Syllables.Count > 0)
                {
                    _karaokeLines.Add(currentLine);
                    currentLine = new KaraokeLine { StartTime = lyric.TimeSec };
                }

                if (currentLine.Syllables.Count == 0) currentLine.StartTime = lyric.TimeSec;
                currentLine.Syllables.Add(new MidiLyric { TimeSec = lyric.TimeSec, Text = txt });
            }
            if (currentLine.Syllables.Count > 0) _karaokeLines.Add(currentLine);
        }

        private void DrawAllLinesToScreen()
        {
            var container = this.FindControl<StackPanel>("ScrollingContainer");
            if (container == null) return;
            container.Children.Clear();

            foreach (var line in _karaokeLines)
            {
                var wrapPanel = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(20) };
                foreach (var syl in line.Syllables) {
                    var tb = new TextBlock { Text = syl.Text, FontSize = 44, FontWeight = FontWeight.Bold, Foreground = ColorFuture };
                    wrapPanel.Children.Add(tb);
                }
                container.Children.Add(wrapPanel);
            }
        }

        private void OnRenderTick(object? sender, EventArgs e)
        {
            if (_midiEngine == null || _karaokeLines.Count == 0) return;
            var container = this.FindControl<StackPanel>("ScrollingContainer");
            if (container == null || container.Children.Count == 0) return;

            // Forza il container a essere largo quanto la finestra per centrare il testo
            container.Width = this.Bounds.Width;

            double currentTime = _midiEngine.CurrentTime;
            int currentIndex = 0;
            for (int i = 0; i < _karaokeLines.Count; i++) {
                if (currentTime >= _karaokeLines[i].StartTime - 0.1) currentIndex = i;
                else break;
            }

            for (int i = 0; i < _karaokeLines.Count; i++)
            {
                var wrapPanel = (WrapPanel)container.Children[i];
                for (int j = 0; j < _karaokeLines[i].Syllables.Count; j++)
                {
                    var syl = _karaokeLines[i].Syllables[j];
                    var tb = (TextBlock)wrapPanel.Children[j];
                    if (i < currentIndex) tb.Foreground = ColorPast; 
                    else if (i > currentIndex) tb.Foreground = ColorFuture; 
                    else { if (currentTime >= syl.TimeSec) tb.Foreground = ColorCurrent; else tb.Foreground = ColorFuture; }
                }
            }

            if (currentIndex < container.Children.Count)
            {
                var targetPanel = (WrapPanel)container.Children[currentIndex];
                // FONDAMENTALE: Posiziona la frase corrente nel primo 25% superiore dello schermo.
                // Così il restante 75% in basso è libero per mostrare tutte le frasi in arrivo!
                double targetY = (this.Bounds.Height * 0.25) - targetPanel.Bounds.Top; 
                _currentScrollY += (targetY - _currentScrollY) * 0.15; 
                Canvas.SetTop(container, _currentScrollY);
            }
        }
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _renderTimer?.Stop();
        }
    } // Chiusura della classe
} // Chiusura del namespace

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using SoulJemApp.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace SoulJemApp.Views
{
    public partial class VisualizerWindow : UserControl
    {
        private MidiPlugin _midiEngine = null!;
        private DispatcherTimer _renderTimer = null!;
        public bool IsItalianNotation { get; set; } = true;
        
        private IEnumerable<Note> _allNotes = new List<Note>();
        private TempoMap? _tempoMap;

        private string[] _notesEng = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        private string[] _notesIta = { "Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si" };

        public VisualizerWindow()
        {
            InitializeComponent();
        }

        public VisualizerWindow(MidiPlugin engine) : this()
        {
            _midiEngine = engine;
            
            // HO RIMESSO IL POPOLAMENTO DEL MENU A TENDINA!
            PopulateTracks();
            LoadMidiNotes();

            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _renderTimer.Tick += OnRenderTick;
            _renderTimer.Start();
        }

        private void LoadMidiNotes()
        {
            if (string.IsNullOrEmpty(_midiEngine.CurrentMidiFile)) return;
            try {
                var midiFile = MidiFile.Read(_midiEngine.CurrentMidiFile);
                _tempoMap = midiFile.GetTempoMap();
                _allNotes = midiFile.GetNotes();
            } catch { }
        }

        private void PopulateTracks()
        {
            var combo = this.FindControl<ComboBox>("TrackCombo");
            if (combo == null) return;
            
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = "TUTTI (Ascolto)" });
            
            for (int i = 0; i < 16; i++)
            {
                string name = $"CH {i + 1} - {_midiEngine.ChannelInstrumentNames[i]}";
                combo.Items.Add(new ComboBoxItem { Content = name });
            }
            combo.SelectedIndex = 0; 
        }

        public void OnLangToggleClick(object sender, RoutedEventArgs e)
        {
            IsItalianNotation = !IsItalianNotation;
            var btn = sender as Button;
            if (btn != null)
            {
                btn.Content = IsItalianNotation ? "ITA" : "ENG";
                btn.Background = SolidColorBrush.Parse(IsItalianNotation ? "#2E7D32" : "#1565C0");
            }
        }

        public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null || _midiEngine == null) return;
            
            int selectedIndex = combo.SelectedIndex;
            if (selectedIndex == 0) 
                for (int i = 0; i < 16; i++) _midiEngine.ChannelMutes[i] = false;
            else 
                for (int i = 0; i < 16; i++) _midiEngine.ChannelMutes[i] = (i == (selectedIndex - 1));
        }

        private void OnRenderTick(object? sender, EventArgs e)
        {
            if (_midiEngine == null || _tempoMap == null) return;

            var canvas = this.FindControl<Canvas>("MainCanvas");
            if (canvas == null) return;

            canvas.Children.Clear();

            double currentTime = _midiEngine.CurrentTime;
            double pxPerSec = 150.0; 
            int selectedChannel = this.FindControl<ComboBox>("TrackCombo")?.SelectedIndex - 1 ?? -1;

            List<int> activeNoteNumbers = new List<int>();
            var visibleNotes = new List<Note>();

            // LA TESTINA DI LETTURA
            var playhead = new Line {
                StartPoint = new Avalonia.Point(150, 0),
                EndPoint = new Avalonia.Point(150, 200),
                Stroke = SolidColorBrush.Parse("#55FFFFFF"),
                StrokeThickness = 1
            };
            canvas.Children.Add(playhead);

            foreach (var note in _allNotes)
            {
                if (selectedChannel >= 0 && note.Channel != selectedChannel) continue;
                if (selectedChannel == -1 && note.Channel == 9) continue; 

                double noteStart = note.TimeAs<MetricTimeSpan>(_tempoMap).TotalMicroseconds / 1000000.0;
                double noteEnd = noteStart + note.LengthAs<MetricTimeSpan>(_tempoMap).TotalMicroseconds / 1000000.0;

                if (noteEnd < currentTime - 1.5) continue; 
                if (noteStart > currentTime + 5.0) continue; 

                visibleNotes.Add(note); 

                double x = 150 + (noteStart - currentTime) * pxPerSec;
                double width = Math.Max(3, (noteEnd - noteStart) * pxPerSec);
                double y = 190 - ((note.NoteNumber - 36) * 2.5); 
                y = Math.Clamp(y, 40, 190); 

                var rect = new Rectangle {
                    Width = width, Height = 2,
                    Fill = GetChannelColor(note.Channel),
                    RadiusX = 1, RadiusY = 1
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvas.Children.Add(rect);

                if (currentTime >= noteStart && currentTime <= noteEnd) activeNoteNumbers.Add(note.NoteNumber);
            }

            string[] names = IsItalianNotation ? _notesIta : _notesEng;
            var noteGroups = visibleNotes.GroupBy(n => Math.Round(n.TimeAs<MetricTimeSpan>(_tempoMap).TotalMicroseconds / 1000000.0 / 0.05) * 0.05).OrderBy(g => g.Key);

            double lastTextEndRow1 = -100;
            double lastTextEndRow2 = -100;

            foreach (var group in noteGroups)
            {
                double groupTime = group.First().TimeAs<MetricTimeSpan>(_tempoMap).TotalMicroseconds / 1000000.0;
                double x = 150 + (groupTime - currentTime) * pxPerSec;
                
                var uniqueNotes = group.Select(n => n.NoteNumber % 12).Distinct().ToList();
                string text = uniqueNotes.Count > 1 ? string.Join("-", uniqueNotes.Select(n => names[n])) : names[uniqueNotes[0]];
                
                var dot = new Ellipse { Fill = SolidColorBrush.Parse("#FFD700"), Width = 8, Height = 8 };
                Canvas.SetLeft(dot, x - 4);
                Canvas.SetTop(dot, 46); 
                canvas.Children.Add(dot);

                double textWidth = text.Length * 11; 
                double startX = x - (text.Length * 5.0);
                double endX = startX + textWidth;
                double yPos = 2; 

                if (startX < lastTextEndRow1 + 15) 
                {
                    yPos = 24; 
                    if (startX < lastTextEndRow2 + 15) continue; 
                    lastTextEndRow2 = endX; 
                }
                else
                {
                    lastTextEndRow1 = endX; 
                }

                var textBlock = new TextBlock {
                    Text = text,
                    Foreground = SolidColorBrush.Parse("#FFD700"), 
                    FontSize = 18, 
                    FontWeight = Avalonia.Media.FontWeight.Bold
                };
                Canvas.SetLeft(textBlock, startX);
                Canvas.SetTop(textBlock, yPos);
                canvas.Children.Add(textBlock);
            }

            // RIAGGIUNTO L'AGGIORNAMENTO DEI TESTI!
            UpdateTextDisplays(activeNoteNumbers);
        }

        private void UpdateTextDisplays(List<int> activeNotes)
        {
            var noteTxt = this.FindControl<TextBlock>("CurrentNoteTxt");
            var chordTxt = this.FindControl<TextBlock>("CurrentChordTxt");

            if (activeNotes.Count == 0)
            {
                if (noteTxt != null) noteTxt.Text = "--";
                if (chordTxt != null) chordTxt.Text = "--";
                return;
            }

            var uniqueNotes = activeNotes.Select(n => n % 12).Distinct().ToList();
            string[] names = IsItalianNotation ? _notesIta : _notesEng;

            if (noteTxt != null) noteTxt.Text = names[uniqueNotes[0]];

            if (chordTxt != null)
            {
                if (uniqueNotes.Count > 1) chordTxt.Text = string.Join("-", uniqueNotes.Select(n => names[n]));
                else chordTxt.Text = "--";
            }
        }

        private IBrush GetChannelColor(int channel)
        {
            string[] colors = { "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#FF00FF", "#00FFFF", "#FFA500", "#800080", "#008000", "#808080", "#FFC0CB", "#A52A2A", "#FFD700", "#4B0082", "#008080", "#000080" };
            return SolidColorBrush.Parse(colors[channel % 16]);
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _renderTimer?.Stop();
        }
    }
}

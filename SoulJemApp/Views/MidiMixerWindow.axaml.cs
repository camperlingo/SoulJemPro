using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Input; // <-- AGGIUNTO PER LA TASTIERA GLOBALE
using SoulJemApp.Plugins;
using System;

namespace SoulJemApp.Views
{
    public partial class MidiMixerWindow : Window
    {
        private MidiPlugin _midiEngine = null!;
        private VisualizerWindow? _rollWin; 
        private TestoWindow? _testoWin;
        private TestoSalaWindow? _salaWin;  
        private bool _isTestoFullScreen = false;

        public MidiMixerWindow() { InitializeComponent(); }

        public MidiMixerWindow(MidiPlugin engine) : this()
        {
            _midiEngine = engine;
            var volSlider = this.FindControl<Slider>("MasterVolumeSlider");
            if (volSlider != null) _midiEngine.MasterVolume = (int)volSlider.Value;
            UpdateGlobals();
            BuildChannels();

            _midiEngine.OnProgressChanged += (percentuale) => {
                Dispatcher.UIThread.Post(() => {
                    var pb = this.FindControl<ProgressBar>("SongProgressBar");
                    if (pb != null) pb.Value = percentuale;
                });
            };

            this.Opened += (s, ev) => {
                _testoWin = new TestoWindow(_midiEngine);
                var tCont = this.FindControl<ContentControl>("TestoContainer");
                if (tCont != null) tCont.Content = _testoWin;

                _rollWin = new VisualizerWindow(_midiEngine);
                var rCont = this.FindControl<ContentControl>("RollContainer");
                if (rCont != null) rCont.Content = _rollWin;
            };

            // LA TASTIERA ONNIPOTENTE (Tunneling)
            // Cattura Spazio, Esc e Invio da QUALSIASI punto della finestra!
            this.AddHandler(InputElement.KeyDownEvent, (s, ev) => {
                if (ev.Key == Key.Space || ev.Key == Key.Escape || ev.Key == Key.Enter)
                {
                    ToggleTestoFullScreen();
                    ev.Handled = true; 
                }
            }, RoutingStrategies.Tunnel);
        }

        // IL TASTO "TESTO" ORA FA DA TELECOMANDO PER IL TUTTO SCHERMO!
        public void OnTestoClick(object sender, RoutedEventArgs e) {
            ToggleTestoFullScreen();
        }

        public void OnSalaClick(object sender, RoutedEventArgs e) {
            if (_salaWin == null || !_salaWin.IsVisible) {
                _salaWin = new TestoSalaWindow(_midiEngine);
                _salaWin.Show(); 
                _salaWin.Closed += (s, ev) => _salaWin = null;
            } else {
                _salaWin.Close();
                _salaWin = null;
            }
        }

        private void UpdateGlobals() { var pitchTxt = this.FindControl<TextBlock>("PitchTxt"); var tempoTxt = this.FindControl<TextBlock>("TempoTxt"); var tempoSlider = this.FindControl<Slider>("TempoSlider"); if (pitchTxt != null) pitchTxt.Text = _midiEngine.PitchShift.ToString(); if (tempoTxt != null) tempoTxt.Text = $"{(int)(_midiEngine.TempoScale * 100)}%"; if (tempoSlider != null && tempoSlider.Value != _midiEngine.TempoScale) tempoSlider.Value = _midiEngine.TempoScale; var btn = this.FindControl<Button>("PlayPauseBtn"); if (btn != null) { btn.Content = _midiEngine.IsPaused ? "▶ PLAY" : "⏸ PAUSA"; btn.Background = SolidColorBrush.Parse(_midiEngine.IsPaused ? "#388E3C" : "#F57C00"); } }
        public void OnPitchMinus(object sender, RoutedEventArgs e) { _midiEngine.PitchShift--; UpdateGlobals(); }
        public void OnPitchPlus(object sender, RoutedEventArgs e) { _midiEngine.PitchShift++; UpdateGlobals(); }
        public void OnTempoSliderChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (_midiEngine == null) return; _midiEngine.TempoScale = e.NewValue; UpdateGlobals(); }
        public void OnMasterVolumeChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (_midiEngine != null) _midiEngine.MasterVolume = (int)e.NewValue; }
        
        public void OnRewindClick(object sender, RoutedEventArgs e) { Console.WriteLine("[MIXER] REWIND!"); if (_midiEngine != null && !string.IsNullOrEmpty(_midiEngine.CurrentMidiFile)) { var pb = this.FindControl<ProgressBar>("SongProgressBar"); if (pb != null) pb.Value = 0; _midiEngine.PlayMidi(_midiEngine.CurrentMidiFile); UpdateGlobals(); } }
        public void OnPlayPauseClick(object sender, RoutedEventArgs e) { _midiEngine.IsPaused = !_midiEngine.IsPaused; UpdateGlobals(); }
        
        private void BuildChannels() { var container = this.FindControl<StackPanel>("ChannelsContainer"); if (container == null) return; for (int i = 0; i < 16; i++) { int channelIndex = i; var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("25, Auto, Auto, Auto"), Margin = new Avalonia.Thickness(0, 3) }; var chLabel = new TextBlock { Text = $"{i + 1}", Foreground = SolidColorBrush.Parse(i == 9 ? "#FF5555" : "#888888"), VerticalAlignment = VerticalAlignment.Center, FontSize = 11, FontWeight = FontWeight.Bold }; Grid.SetColumn(chLabel, 0); var nameLabel = new TextBlock { Text = $"CH {i + 1} - {_midiEngine.ChannelInstrumentNames[i]}", Foreground = SolidColorBrush.Parse(i == 9 ? "#FFCC00" : "#CCCCCC"), VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Width = 140 }; Grid.SetColumn(nameLabel, 1); var muteCb = new CheckBox { IsChecked = _midiEngine.ChannelMutes[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(5, 0) }; Grid.SetColumn(muteCb, 2); var statusTxt = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 10, FontWeight = FontWeight.Bold }; Grid.SetColumn(statusTxt, 3); Action<bool> updateStatus = (isMuted) => { statusTxt.Text = isMuted ? "🔴 MUTO" : "🟢 AUDIO"; statusTxt.Foreground = SolidColorBrush.Parse(isMuted ? "#D32F2F" : "#388E3C"); }; updateStatus(_midiEngine.ChannelMutes[i]); muteCb.IsCheckedChanged += (s, ev) => { bool isMuted = muteCb.IsChecked ?? false; _midiEngine.ChannelMutes[channelIndex] = isMuted; updateStatus(isMuted); }; grid.Children.Add(chLabel); grid.Children.Add(nameLabel); grid.Children.Add(muteCb); grid.Children.Add(statusTxt); container.Children.Add(grid); } }

        public void OnStopClick(object sender, RoutedEventArgs e) { if (_midiEngine != null) { _midiEngine.StopPlayback(); } this.Close(); }

        protected override void OnClosed(EventArgs e) { base.OnClosed(e); _salaWin?.Close(); if (_midiEngine != null) { _midiEngine.StopPlayback(); _midiEngine.StopEngine(); } if (this.Owner is MainWindow mainWin) { Dispatcher.UIThread.Post(() => { mainWin.OnPreviewStopClick(this, new RoutedEventArgs()); }); } }
        
        public void OnTestoDoubleTapped(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            ToggleTestoFullScreen();
        }

        private void ToggleTestoFullScreen()
        {
            _isTestoFullScreen = !_isTestoFullScreen;

            var mainGrid = this.FindControl<Grid>("MainGrid");
            var leftGrid = this.FindControl<Grid>("LeftGrid");
            var mixerBorder = this.FindControl<Border>("MixerBorder");
            var rollBorder = this.FindControl<Border>("RollBorder");

            if (mainGrid == null || leftGrid == null || mixerBorder == null || rollBorder == null) return;

            if (_isTestoFullScreen)
            {
                mainGrid.ColumnDefinitions[1].Width = new GridLength(0);       
                leftGrid.RowDefinitions[1].Height = new GridLength(0);     
                mixerBorder.IsVisible = false;
                rollBorder.IsVisible = false;
            }
            else
            {
                mainGrid.ColumnDefinitions[1].Width = new GridLength(360);     
                leftGrid.RowDefinitions[1].Height = new GridLength(200);   
                mixerBorder.IsVisible = true;
                rollBorder.IsVisible = true;
            }
        }
    }
}

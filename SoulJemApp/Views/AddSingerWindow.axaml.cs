using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SoulJemApp.Models;
using System;
using System.IO;

namespace SoulJemApp.Views
{
    public partial class AddSingerWindow : Window
    {
        private SingerItem? _editingSinger = null;

        public AddSingerWindow()
        {
            InitializeComponent();
        }

        // COSTRUTTORE MIGLIORATO: Ora accetta anche il titolo noto (es. dalla Omnibar!)
        public AddSingerWindow(string filePath, string knownTitle = "") : this()
        {
            var pathInput = this.FindControl<TextBox>("PathInput");
            var titleInput = this.FindControl<TextBox>("SongTitleInput");

            if (pathInput != null && !string.IsNullOrEmpty(filePath))
            {
                pathInput.Text = filePath;
                
                if (titleInput != null)
                {
                    if (!string.IsNullOrEmpty(knownTitle)) {
                        titleInput.Text = knownTitle; // Se l'app sa il titolo, lo scrive!
                    }
                    else if (filePath.StartsWith("http")) {
                        titleInput.Text = "Video Web (Scrivi qui il Titolo!)";
                    }
                    else {
                        titleInput.Text = Path.GetFileNameWithoutExtension(filePath); 
                    }
                }
            }
        }

        public AddSingerWindow(SingerItem singerToEdit) : this()
        {
            _editingSinger = singerToEdit;
            
            var nameInput = this.FindControl<TextBox>("SingerNameInput");
            var titleInput = this.FindControl<TextBox>("SongTitleInput");
            var pathInput = this.FindControl<TextBox>("PathInput");
            var pitchSlider = this.FindControl<Slider>("PitchSlider");

            if (nameInput != null) nameInput.Text = singerToEdit.Name;
            if (titleInput != null) titleInput.Text = singerToEdit.SongTitle; 
            if (pathInput != null) pathInput.Text = singerToEdit.SongPath;
            if (pitchSlider != null) pitchSlider.Value = singerToEdit.Pitch;
        }

        // --- IL NUOVO TASTO WWW ---
        public void OnSearchWebClick(object sender, RoutedEventArgs e)
        {
            var titleInput = this.FindControl<TextBox>("SongTitleInput");
            string query = titleInput?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(query) || query == "Video Web (Scrivi qui il Titolo!)") return;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWin)
            {
                // Aggiunge in automatico "karaoke" davanti alla richiesta del cliente!
                string finalQuery = "karaoke " + query;
                
                // Apre la finestra di ricerca e INIETTA il risultato direttamente in questo popup!
                var searchWin = new YouTubeSearchWindow(finalQuery, "ytsearch", mainWin._ytdlpEngine, false, (selectedUrl) => 
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                    {
                        var pathInput = this.FindControl<TextBox>("PathInput");
                        if (pathInput != null && !string.IsNullOrEmpty(selectedUrl)) 
                        {
                            pathInput.Text = selectedUrl;
                        }
                    });
                });
                searchWin.Show();
            }
        }

        public async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Seleziona Base", AllowMultiple = false });
            if (files != null && files.Count > 0)
            {
                string path = files[0].Path.LocalPath;
                var pathInput = this.FindControl<TextBox>("PathInput");
                if (pathInput != null) pathInput.Text = path;

                var titleInput = this.FindControl<TextBox>("SongTitleInput");
                if (titleInput != null) titleInput.Text = Path.GetFileNameWithoutExtension(path);
            }
        }

        public void PitchMinus_Click(object sender, RoutedEventArgs e)
        {
            var pitchSlider = this.FindControl<Slider>("PitchSlider");
            if (pitchSlider != null && pitchSlider.Value > pitchSlider.Minimum) pitchSlider.Value -= 1;
        }

        public void PitchPlus_Click(object sender, RoutedEventArgs e)
        {
            var pitchSlider = this.FindControl<Slider>("PitchSlider");
            if (pitchSlider != null && pitchSlider.Value < pitchSlider.Maximum) pitchSlider.Value += 1;
        }

        private void OnAddToPlaylist(object sender, RoutedEventArgs e)
        {
            var nameInput = this.FindControl<TextBox>("SingerNameInput")?.Text ?? "Anonimo";
            var pathInput = this.FindControl<TextBox>("PathInput")?.Text ?? "";
            var titleInput = this.FindControl<TextBox>("SongTitleInput")?.Text?.Trim() ?? "";
            var pitchSlider = this.FindControl<Slider>("PitchSlider");
            
            int pitch = pitchSlider != null ? (int)pitchSlider.Value : 0;
            
            string title = titleInput;
            if (string.IsNullOrEmpty(title)) 
            {
                title = pathInput.StartsWith("http") ? "URL Web Sconosciuto" : Path.GetFileNameWithoutExtension(pathInput);
            }

            // SE STIAMO MODIFICANDO: aggiorna i dati vecchi e RILANCIA IL DOWNLOAD
            if (_editingSinger != null)
            {
                bool pathChanged = _editingSinger.SongPath != pathInput;
                bool pitchChanged = _editingSinger.Pitch != pitch;
                bool wasWaiting = _editingSinger.Status.Contains("CERCARE") || _editingSinger.Status.Contains("ATTESA");

                _editingSinger.Name = nameInput;
                _editingSinger.SongPath = pathInput;
                _editingSinger.SongTitle = title; 
                _editingSinger.Pitch = pitch;
                
                // Se abbiamo incollato un link nuovo o cambiato il pitch, riavviamo la procedura!
                if (pathChanged || pitchChanged || (wasWaiting && pathInput.StartsWith("http")))
                {
                    _editingSinger.Status = "DA ELABORARE";
                    
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWin)
                    {
                        _ = mainWin.ProcessSingerBackground(_editingSinger);
                    }
                }
                else
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWin)
                    {
                        var grid = mainWin.FindControl<DataGrid>("SingerGrid");
                        if (grid != null) { grid.ItemsSource = null; grid.ItemsSource = mainWin.SingersQueue; }
                    }
                }
                
                Console.WriteLine($"[SISTEMA] Cantante '{nameInput}' aggiornato!");
            }
            else
            {
                var newSinger = new SingerItem
                {
                    Name = nameInput,
                    SongPath = pathInput,
                    SongTitle = title, 
                    Pitch = pitch,
                    Status = "IN ATTESA",
                    ProgressValue = 0
                };

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWin)
                {
                    mainWin.SingersQueue.Add(newSinger);
                    Console.WriteLine($"[SISTEMA] Cantante '{nameInput}' aggiunto!");
                    _ = mainWin.ProcessSingerBackground(newSinger);
                }
            }

            this.Close();
        }

        private void OnPianoBarInstant(object sender, RoutedEventArgs e)
        {
            var nameInput = this.FindControl<TextBox>("SingerNameInput")?.Text ?? "Piano Bar";
            var pathInput = this.FindControl<TextBox>("PathInput")?.Text ?? "";
            var titleInput = this.FindControl<TextBox>("SongTitleInput")?.Text?.Trim() ?? "";
            var pitchSlider = this.FindControl<Slider>("PitchSlider");
            
            int pitch = pitchSlider != null ? (int)pitchSlider.Value : 0;
            if (string.IsNullOrWhiteSpace(pathInput)) return;

            Console.WriteLine("[POPUP] Avvio PIANO BAR immediato! (Scavalco la coda)");

            string title = titleInput;
            if (string.IsNullOrEmpty(title)) 
            {
                title = pathInput.StartsWith("http") ? "Brano Web / Diretto" : Path.GetFileNameWithoutExtension(pathInput);
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWin)
            {
                var instantSinger = new SingerItem 
                { 
                    Name = nameInput, 
                    SongTitle = title, 
                    SongPath = pathInput, 
                    Pitch = pitch,
                    Status = "PRONTA",
                    ProgressColor = Avalonia.Media.Brushes.LimeGreen,
                    ProgressValue = 100
                };

                mainWin.SingersQueue.Insert(0, instantSinger);
                var grid = mainWin.FindControl<DataGrid>("SingerGrid");
                if (grid != null) grid.SelectedItem = instantSinger;

                mainWin.OnAvviaBranoClick(this, new RoutedEventArgs());
            }

            this.Close();
        }
    }
}

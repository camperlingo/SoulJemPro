using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SoulJemApp.Plugins;
using SoulJemApp.Models;

namespace SoulJemApp.Views
{
    public partial class StudioDownloaderWindow : Window
    {
        private YtdlpPlugin _ytdlp = new YtdlpPlugin();
        // SOSTITUITO QUI:
        private List<YouTubeResultItem> _currentResults = new List<YouTubeResultItem>();

        public StudioDownloaderWindow()
        {
            InitializeComponent();
            
            string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SoulJem_v5", "MP3_DOWNLOADS");
            if (!Directory.Exists(defaultDir)) Directory.CreateDirectory(defaultDir);
            
            var folderInput = this.FindControl<TextBox>("FolderInput");
            if (folderInput != null) folderInput.Text = defaultDir;
        }

        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Seleziona Cartella di Destinazione", AllowMultiple = false });
            if (folders != null && folders.Count > 0)
            {
                var folderInput = this.FindControl<TextBox>("FolderInput");
                if (folderInput != null) folderInput.Text = folders[0].Path.LocalPath;
            }
        }

        private async void OnSearchClick(object sender, RoutedEventArgs e) => await PerformSearch();
        private async void OnSearchKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await PerformSearch(); }

        private async Task PerformSearch()
        {
            var searchInput = this.FindControl<TextBox>("SearchInput");
            string query = searchInput?.Text ?? "";
            if (string.IsNullOrWhiteSpace(query)) return;

            var searchBtn = this.FindControl<Button>("SearchBtn");
            if (searchBtn != null)
            {
                searchBtn.Content = "Ricerco...";
                searchBtn.IsEnabled = false;
            }

            var results = await _ytdlp.SearchAsync(query, 10);
            _currentResults = results;
            
            var list = this.FindControl<ListBox>("ResultsList");
            if (list != null) list.ItemsSource = _currentResults;

            if (searchBtn != null)
            {
                searchBtn.Content = "Cerca";
                searchBtn.IsEnabled = true;
            }
        }

        // Il doppio click ora riempie sia URL che Nome in automatico!
        private void OnResultDoubleTapped(object sender, TappedEventArgs e)
        {
            var list = this.FindControl<ListBox>("ResultsList");
            // SOSTITUITO ANCHE QUI:
            if (list?.SelectedItem is YouTubeResultItem selected)
            {
                var urlInput = this.FindControl<TextBox>("UrlInput");
                if (urlInput != null) urlInput.Text = selected.Url;
                
                SetTitleAndEnable(selected.Title);
            }
        }

        private async void OnInfoClick(object sender, RoutedEventArgs e)
        {
            var urlInput = this.FindControl<TextBox>("UrlInput");
            string url = urlInput?.Text ?? "";
            if (string.IsNullOrWhiteSpace(url)) return;

            var infoBtn = this.FindControl<Button>("InfoBtn");
            var statusLabel = this.FindControl<TextBlock>("StatusLabel");

            if (infoBtn != null) infoBtn.IsEnabled = false;
            if (statusLabel != null) statusLabel.Text = "⏳ Analisi del link in corso...";

            string title = await _ytdlp.GetTitleFromUrlAsync(url);
            if (!string.IsNullOrEmpty(title))
            {
                SetTitleAndEnable(title);
            }
            else
            {
                if (statusLabel != null) statusLabel.Text = "⚠️ Impossibile leggere il titolo, scrivilo a mano!";
                var btn = this.FindControl<Button>("DownloadBtn");
                if (btn != null)
                {
                    btn.IsEnabled = true;
                    btn.Background = Avalonia.Media.SolidColorBrush.Parse("#4CAF50");
                }
            }
            if (infoBtn != null) infoBtn.IsEnabled = true;
        }

        private void SetTitleAndEnable(string rawTitle)
        {
            string cleanTitle = new string(rawTitle.Where(c => char.IsLetterOrDigit(c) || " .-_()".Contains(c)).ToArray()).Trim();
            
            var nameInput = this.FindControl<TextBox>("FileNameInput");
            if (nameInput != null) nameInput.Text = cleanTitle;
            
            var btn = this.FindControl<Button>("DownloadBtn");
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Background = Avalonia.Media.SolidColorBrush.Parse("#4CAF50");
            }
            
            var statusLabel = this.FindControl<TextBlock>("StatusLabel");
            if (statusLabel != null) statusLabel.Text = "✅ Brano caricato! Scegli la qualità e clicca su SCARICA ORA.";
        }

        private async void OnDownloadClick(object sender, RoutedEventArgs e)
        {
            var urlInput = this.FindControl<TextBox>("UrlInput");
            var nameInput = this.FindControl<TextBox>("FileNameInput");
            var folderInput = this.FindControl<TextBox>("FolderInput");
            
            string url = urlInput?.Text ?? "";
            string fileName = nameInput?.Text ?? "";
            string folder = folderInput?.Text ?? "";

            var fmtCombo = this.FindControl<ComboBox>("FmtCombo");
            var bitCombo = this.FindControl<ComboBox>("BitrateCombo");
            var hzCombo = this.FindControl<ComboBox>("HzCombo");

            string fmt = "mp3";
            if (fmtCombo != null && fmtCombo.SelectedItem is ComboBoxItem fItem && fItem.Content != null) 
                fmt = fItem.Content.ToString() ?? "mp3";

            string bitrate = "320";
            if (bitCombo != null && bitCombo.SelectedItem is ComboBoxItem bItem && bItem.Content != null) 
                bitrate = bItem.Content.ToString() ?? "320";

            string hz = "48000";
            if (hzCombo != null && hzCombo.SelectedItem is ComboBoxItem hItem && hItem.Content != null) 
                hz = hItem.Content.ToString() ?? "48000";

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(fileName)) return;

            var btn = this.FindControl<Button>("DownloadBtn");
            var progress = this.FindControl<ProgressBar>("DownloadProgress");
            var percentLbl = this.FindControl<TextBlock>("PercentLabel");
            var status = this.FindControl<TextBlock>("StatusLabel");

            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Background = Avalonia.Media.SolidColorBrush.Parse("#B0BEC5");
            }
            if (progress != null) 
            {
                progress.IsIndeterminate = false; // Ci assicuriamo che sia normale
                progress.Value = 0;
            }
            if (percentLbl != null) percentLbl.Text = "0%";
            if (status != null) status.Text = "🚀 Avvio download...";

            // Lancia il motore
            string result = await _ytdlp.DownloadAudioCustomAsync(url, folder, fileName, fmt, bitrate, hz, (p) => {
                Dispatcher.UIThread.Post(() => { 
                    if (progress != null) 
                    {
                        if (p < 100) 
                        {
                            progress.IsIndeterminate = false;
                            progress.Value = p;
                            if (percentLbl != null) percentLbl.Text = $"{p}%";
                            if (status != null) status.Text = "⬇️ Scaricamento file in corso...";
                        }
                        else 
                        {
                            // ATTIVA L'EFFETTO ANIMATO PER L'ESTRAZIONE!
                            progress.IsIndeterminate = true; 
                            if (percentLbl != null) percentLbl.Text = "ESTRAZIONE";
                            if (status != null) status.Text = "⚙️ FFmpeg sta spremendo l'audio HQ (il PC sta sudando), attendi!";
                        }
                    }
                });
            });

            // A fine processo (successo o errore), disattiviamo l'animazione
            Dispatcher.UIThread.Post(() => {
                if (progress != null) progress.IsIndeterminate = false;

                if (!string.IsNullOrEmpty(result))
                {
                    if (status != null) status.Text = "✅ TUTTO FATTO! File pronto in archivio.";
                    if (percentLbl != null) percentLbl.Text = "COMPLETATO";
                    if (progress != null) progress.Value = 100;
                }
                else
                {
                    if (status != null) status.Text = "❌ Errore durante il processo. Verifica il link.";
                    if (btn != null)
                    {
                        btn.IsEnabled = true;
                        btn.Background = Avalonia.Media.SolidColorBrush.Parse("#4CAF50");
                    }
                }
            });
        }
    }
}

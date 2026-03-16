using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SoulJemApp.Models;
using SoulJemApp.Plugins;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SoulJemApp.Views
{
    public partial class YouTubeSearchWindow : Window
    {
        public string? SelectedUrl { get; private set; } = null;
        private string _query = "";
        private string _prefix = "ytsearch";
        private YtdlpPlugin? _ytdlp;
        private YouTubeSearchSalaWindow? _salaWindow;
        private bool _showOnTv = true; 
        
        private Action<string>? _onSelectCallback; 

        public YouTubeSearchWindow() { InitializeComponent(); }

        public YouTubeSearchWindow(string query, string prefix, YtdlpPlugin ytdlp, bool showOnTv, Action<string> onSelectCallback) : this()
        {
            _query = query;
            _prefix = prefix;
            _ytdlp = ytdlp;
            _showOnTv = showOnTv;
            _onSelectCallback = onSelectCallback;
            
            var titleText = this.FindControl<TextBlock>("TitleText");
            if (titleText != null) titleText.Text = $"⏳ Ricerca in corso per: '{query}'...";
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (_showOnTv)
            {
                _salaWindow = new YouTubeSearchSalaWindow();
                
                if (this.Screens.All.Count > 1)
                {
                    _salaWindow.Position = this.Screens.All[1].Bounds.Position;
                    _salaWindow.WindowState = WindowState.FullScreen;
                }
                else
                {
                    _salaWindow.Width = 1200;
                    _salaWindow.Height = 700;
                    _salaWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                _salaWindow.Show();
            }

            Task.Run(async () => await PerformSearchOffThread());
        }

        private async Task PerformSearchOffThread()
        {
            if (_ytdlp == null) return;

            var results = await _ytdlp.SearchAsync(_query, 10);
            
            Dispatcher.UIThread.Post(() => {
                var titleText = this.FindControl<TextBlock>("TitleText");
                if (titleText != null) titleText.Text = $"✅ Risultati per: '{_query}'";

                var listBox = this.FindControl<ListBox>("ResultsList");
                if (listBox != null) 
                {
                    listBox.ItemsSource = results;
                    _ = LoadThumbnailsAsync(results, listBox);
                }

                _salaWindow?.UpdateResults(_query, results);
            });
        }

        private async Task LoadThumbnailsAsync(List<YouTubeResultItem> results, ListBox listBox)
        {
            var tasks = new List<Task>();
            foreach (var item in results) tasks.Add(item.LoadImageAsync());
            await Task.WhenAll(tasks);
            
            Dispatcher.UIThread.Post(() => {
                listBox.ItemsSource = null;
                listBox.ItemsSource = results;
                _salaWindow?.UpdateResults(_query, results);
            });
        }

        public void OnSelectClick(object sender, RoutedEventArgs e)
        {
            string url = "";
            
            if (sender is Button btn && btn.CommandParameter is string btnUrl) 
                url = btnUrl;
            else
            {
                var listBox = this.FindControl<ListBox>("ResultsList");
                if (listBox?.SelectedItem is YouTubeResultItem selected) url = selected.Url;
            }

            if (!string.IsNullOrEmpty(url))
            {
                SelectedUrl = url;
                _onSelectCallback?.Invoke(url);
                this.Close(); 
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _salaWindow?.Close();
        }
    }
}

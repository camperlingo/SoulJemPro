using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Collections.ObjectModel;
using System.IO;
using System;

namespace SoulJemApp.Views
{
    public class HistoryItem
    {
        public string Time { get; set; } = "";
        public string SingerName { get; set; } = "";
        public string SongTitle { get; set; } = "";
        public string SongPath { get; set; } = "";
        
        // LA NOSTRA MAGIA DELLE 8 ORE:
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }

    public partial class HistoryWindow : Window
    {
        public ObservableCollection<HistoryItem>? HistoryList { get; set; }
        private MainWindow? _mainWindow;

        public HistoryWindow()
        {
            InitializeComponent();
        }

        public HistoryWindow(ObservableCollection<HistoryItem> history, MainWindow mainWin) : this()
        {
            HistoryList = history;
            _mainWindow = mainWin;
            
            var grid = this.FindControl<DataGrid>("HistoryGrid");
            if (grid != null) grid.ItemsSource = HistoryList;
        }

        private void ReloadButton_Click(object? sender, RoutedEventArgs e)
        {
            var grid = this.FindControl<DataGrid>("HistoryGrid");
            if (grid?.SelectedItem is HistoryItem selected && _mainWindow != null)
            {
                _mainWindow.AddSingerToQueue(selected.SingerName, selected.SongTitle, selected.SongPath);
                this.Close();
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) => this.Close();

        // 1. DOPPIO CLIC: Riapre il Popup Cantante con quella base!
        public void OnHistoryDoubleTapped(object sender, Avalonia.Input.TappedEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid?.SelectedItem is HistoryItem selected)
            {
                var addWin = new AddSingerWindow(selected.SongPath);
                addWin.ShowDialog(this);
            }
        }

        // 2. TASTO DESTRO: Salva la base dove vuoi (Es. su Pennetta USB)!
        public async void OnSaveBaseClick(object? sender, RoutedEventArgs e)
        {
            var grid = this.FindControl<DataGrid>("HistoryGrid");
            if (grid?.SelectedItem is HistoryItem selected && !string.IsNullOrEmpty(selected.SongPath) && File.Exists(selected.SongPath))
            {
                var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Salva Base nel PC",
                    SuggestedFileName = Path.GetFileName(selected.SongPath)
                });

                if (file != null)
                {
                    // Copia fisicamente il file nella nuova destinazione!
                    File.Copy(selected.SongPath, file.Path.LocalPath, true);
                    Console.WriteLine($"[SISTEMA] Base esportata con successo: {file.Path.LocalPath}");
                }
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using SoulJemApp.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace SoulJemApp.Views
{
    public partial class RadioManagerWindow : Window
    {
        public ObservableCollection<RadioItem> Radios { get; set; } = new ObservableCollection<RadioItem>();
        private string _filePath;

        public RadioManagerWindow()
        {
            InitializeComponent();
            
            // Salviamo il file nella tua cartella sicura SoulJem_v5
            _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SoulJem_v5", "radios.json");
            
            LoadRadios();
            
            var grid = this.FindControl<DataGrid>("RadioGrid");
            if (grid != null) grid.ItemsSource = Radios;
        }

        private void LoadRadios()
        {
            if (!File.Exists(_filePath))
            {
                // Se è la prima volta, mettiamo due radio di base
                Radios.Add(new RadioItem { Name = "RTL 102.5", Url = "https://shoutcast.rtl.it/stream1" });
                Radios.Add(new RadioItem { Name = "RDS", Url = "https://icstream.rds.radio/rds" });
                return;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<ObservableCollection<RadioItem>>(json);
                if (list != null) Radios = list;
            }
            catch { }
        }

        private void AddRadio_Click(object? sender, RoutedEventArgs e)
        {
            Radios.Add(new RadioItem { Name = "Nuova Radio", Url = "http://" });
        }

        private void Delete_Click(object? sender, RoutedEventArgs e)
        {
            var grid = this.FindControl<DataGrid>("RadioGrid");
            if (grid?.SelectedItem is RadioItem radio)
            {
                Radios.Remove(radio);
            }
        }

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string json = JsonSerializer.Serialize(Radios, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRORE] Salvataggio radio fallito: {ex.Message}");
            }
            Close(); // Chiude la finestra dopo aver salvato
        }
    }
}

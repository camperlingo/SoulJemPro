using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace SoulJemApp.Models
{
    public class SingerItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string SongTitle { get; set; } = "";
        public string SongPath { get; set; } = "";

        // --- PATCH AGGIORNAMENTO VISIVO PITCH ---
        private int _pitch = 0;
        public int Pitch
        {
            get => _pitch;
            set
            {
                if (_pitch != value)
                {
                    _pitch = value;
                    // Questo comando urla ad Avalonia: "EHI! IL PITCH È CAMBIATO, AGGIORNA LO SCHERMO!"
                    OnPropertyChanged(nameof(Pitch)); 
                }
            }
        }
        // ----------------------------------------

        private string _status = "IN ATTESA";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private int _progressValue = 0;
        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private IBrush _progressColor = Brushes.Transparent;
        public IBrush ProgressColor
        {
            get => _progressColor;
            set { _progressColor = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

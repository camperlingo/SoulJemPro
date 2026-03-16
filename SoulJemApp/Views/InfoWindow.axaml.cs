using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SoulJemApp.Views
{
    public partial class InfoWindow : Window
    {
        public InfoWindow()
        {
            InitializeComponent();
        }

        public void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

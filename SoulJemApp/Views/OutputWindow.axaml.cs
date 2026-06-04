using Avalonia.Controls;

namespace SoulJemApp.Views
{
    public partial class OutputWindow : Window
    {
        public OutputWindow()
        {
            InitializeComponent();
        }

        // Questo metodo serve al Regista per passare il video a questo schermo
        public Controls.PreviewControl? GetPublicScreen()
        {
            return this.FindControl<Controls.PreviewControl>("PublicVideoSurface");
        }
    }
}

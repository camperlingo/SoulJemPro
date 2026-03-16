using Avalonia.Controls;
using SoulJemApp.Models;
using System.Collections.Generic;

namespace SoulJemApp.Views
{
    public partial class YouTubeSearchSalaWindow : Window
    {
        public YouTubeSearchSalaWindow()
        {
            InitializeComponent();
        }

        public void UpdateResults(string query, List<YouTubeResultItem> results)
        {
            var titleText = this.FindControl<TextBlock>("TitleText");
            if (titleText != null) titleText.Text = $"🔍 RISULTATI PER: '{query.ToUpper()}'";

            var listBox = this.FindControl<ListBox>("ResultsList");
            if (listBox != null)
            {
                listBox.ItemsSource = null;
                listBox.ItemsSource = results;
            }
        }
    }
}

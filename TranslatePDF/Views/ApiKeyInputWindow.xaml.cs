using System.Windows;

namespace TranslatePDF.Views
{
    public partial class ApiKeyInputWindow : Window
    {
        public ApiKeyInputWindow()
        {
            InitializeComponent();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ApiKeyBox.Text))
            {
                MessageBox.Show(
                    "APIキーを入力してください",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Properties.Settings.Default.DeepLApiKey =
                ApiKeyBox.Text.Trim();

            Properties.Settings.Default.Save();

            DialogResult = true;
            Close();
        }
    }
}
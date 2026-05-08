using System.Windows;
using TranslatePDF.Services;

namespace TranslatePDF
{
    public partial class ApiKeyWindow : Window
    {
        public ApiKeyWindow()
        {
            InitializeComponent();

            // 現在のキーを表示（任意）
            ApiKeyTextBox.Text =
                Properties.Settings.Default.DeepLApiKey;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var newKey = ApiKeyTextBox.Text?.Trim();

            try
            {
                newKey = newKey?.Trim() ?? "";

                DeepLService.UpdateApiKey(newKey);

                if (!string.IsNullOrWhiteSpace(newKey))
                {
                    DeepLService.Reset();
                    DeepLService.Initialize();
                }
                else
                {
                    DeepLService.Reset();
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

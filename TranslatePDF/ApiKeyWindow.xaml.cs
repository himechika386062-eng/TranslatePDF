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

            if (string.IsNullOrWhiteSpace(newKey))
            {
                MessageBox.Show(
                    "APIキーを入力してください",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                // ここで保存
                DeepLService.UpdateApiKey(newKey);

                // 重要：再初期化
                DeepLService.Reset();
                DeepLService.Initialize();

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

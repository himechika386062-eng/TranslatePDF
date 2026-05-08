using System.Text;
using System.Windows;
using TranslatePDF.Services;
using TranslatePDF.Views;

namespace TranslatePDF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            base.OnStartup(e);

            var mainWindow = new MainWindow();

            MainWindow = mainWindow;

            // ★ APIキーが未設定なら入力させる
            if (!DeepLService.IsApiKeySet)
            {
                var dialog = new ApiKeyInputWindow();
                
                if (dialog.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            // ★ APIキー確定後に初期化
            DeepLService.Initialize();

            mainWindow.Show();
        }
    }

}

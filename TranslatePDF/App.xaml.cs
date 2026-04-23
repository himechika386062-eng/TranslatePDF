using System.Configuration;
using System.Data;
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
            base.OnStartup(e);


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
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }

}

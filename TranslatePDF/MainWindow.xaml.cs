//#define SKIPTRANSLATE
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout.Element;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TranslatePDF.Services;

namespace TranslatePDF
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cts;
        private ObservableCollection<string> selectedPdfPaths = new ObservableCollection<string>();

        public MainWindow()
        {
            InitializeComponent();
            SideBySideCheckBox.IsChecked = Properties.Settings.Default.SideBySide;
        }

        // PDF選択ボタン
        private void SelectPdfButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "PDF files (*.pdf)|*.pdf";
            dialog.Multiselect = true;

            if (dialog.ShowDialog() == true)
            {
                foreach (var pdf in dialog.FileNames)
                {
                    if (!selectedPdfPaths.Contains(pdf))
                    {
                        selectedPdfPaths.Add(pdf);
                        SelectedPdfList.Items.Add(pdf);
                    }
                }
            }
        }

        // 翻訳開始ボタン
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedPdfPaths.Count == 0)
            {
                MessageBox.Show("PDFを選択してください");
                return;
            }

            bool attachOriginal = SideBySideCheckBox.IsChecked == true;

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var selectedItem = (ComboBoxItem)LanguageComboBox.SelectedItem;
            var targetLang = selectedItem.Tag.ToString()!;

            StartButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            ProgressText.Text = "";
            CurrentPdfText.Visibility = Visibility.Visible;
            ProgressBar.Visibility = Visibility.Visible;
            ProgressText.Visibility = Visibility.Visible;
            int totalPdfCount = selectedPdfPaths.Count;
            int finishedPdfCount = 0;
            try
            {
                foreach (var pdfPath in selectedPdfPaths.ToList())
                {
                    CurrentPdfText.Visibility = Visibility.Visible;

                    CurrentPdfText.Text =
                        $"PDF {finishedPdfCount + 1}/{totalPdfCount} : {Path.GetFileName(pdfPath)} を翻訳中";
                    await Task.Yield();

                    var paragraphs = PdfReaderService.ReadLines(pdfPath);
#if SKIPTRANSLATE
#else
                    ProgressBar.Minimum = 0;
                    ProgressBar.Maximum = paragraphs.Count;
                    ProgressBar.Value = 0;
                    ProgressBar.Visibility = Visibility.Visible;
                    ProgressText.Visibility = Visibility.Visible;

                    int current = 0;

                    // ★ 並列数（DeepL Freeなら3〜5推奨）
                    int maxParallel = 3;
                    using var semaphore = new SemaphoreSlim(maxParallel);
                    
                    bool errorShown = false;
                    object errorLock = new object();
                    
                    var tasks = paragraphs.Select(async paragraph =>
                    {
                        await semaphore.WaitAsync(token);

                        try
                        {
                            token.ThrowIfCancellationRequested();

                            string text = paragraph.Text;

                            var translated = await DeepLService.TranslateAsync(
                                text,
                                targetLang,
                                token);

                            paragraph.TranslatedText = translated;

                            Dispatcher.Invoke(() =>
                            {
                                current++;
                                ProgressBar.Value = current;
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            
                        }
                        catch (Exception ex)
                        {
                            bool shouldShow = false;

                            lock (errorLock)
                            {
                                if (!errorShown)
                                {
                                    errorShown = true;
                                    shouldShow = true;
                                }
                            }

                            if (shouldShow)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    if (ex.Message == "QUOTA_EXCEEDED")
                                    {
                                        MessageBox.Show(
                                            "翻訳の利用上限に達しました。\n\n" +
                                            "しばらく時間をおいてから再度お試しください。",
                                            "翻訳できません",
                                            MessageBoxButton.OK,
                                            MessageBoxImage.Warning);
                                    }
                                    else
                                    {
                                        MessageBox.Show(
                                            ex.Message,
                                            "エラー",
                                            MessageBoxButton.OK,
                                            MessageBoxImage.Error);
                                    }
                                });
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks);
#endif
                    // ===== PDF生成 =====
                    var textBlocks = paragraphs.ToList();

                    var outputPdf = CreatePdfWithLayoutRects(
                        pdfPath,
                        targetLang,
                        textBlocks,
                        SideBySideCheckBox.IsChecked == true);

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = outputPdf,
                        UseShellExecute = true
                    });
                    finishedPdfCount++;
                    Dispatcher.Invoke(() =>
                    {
                        selectedPdfPaths.Remove(pdfPath);
                        SelectedPdfList.Items.Remove(pdfPath);
                    });
                    if (SelectedPdfList.Items.Count == 0)
                    {
                        ProgressText.Text = "すべて完了";
                    }
                    CurrentPdfText.Text =
                        $"PDF進捗 {finishedPdfCount}/{totalPdfCount} : {Path.GetFileName(pdfPath)} 完了"; 
                }
            }
            catch (OperationCanceledException)
            {
                CurrentPdfText.Text = "翻訳がキャンセルされました";
                MessageBox.Show("翻訳がキャンセルされました",
                    "キャンセル",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラーが発生しました:\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _cts = null;
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        #region ドラッグ＆ドロップ
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;

            var pdfs = files
                .Where(f => Path.GetExtension(f).ToLower() == ".pdf");

            foreach (var pdf in pdfs)
            {
                if (!selectedPdfPaths.Contains(pdf))
                {
                    selectedPdfPaths.Add(pdf);
                    SelectedPdfList.Items.Add(pdf);
                }
            }
        }
        #endregion

       
        public static string CreatePdfWithLayoutRects(string originalPdfPath,string targetLang, List<TextBlock> textBlocks, bool sideBySide)
        {
            string output =　sideBySide ?
                Path.Combine(
                    Path.GetDirectoryName(originalPdfPath)!,
                    Path.GetFileNameWithoutExtension(originalPdfPath)
                    + "_" + targetLang + "_with_original.pdf") :
                Path.Combine(
                        Path.GetDirectoryName(originalPdfPath)!,
                        Path.GetFileNameWithoutExtension(originalPdfPath)
                        + "_" + targetLang + ".pdf");

            using var reader = new PdfReader(originalPdfPath);
            using var writer = new PdfWriter(output);
            using var srcPdf = new PdfDocument(reader);
            using var destPdf = new PdfDocument(writer);

            int pageCount = srcPdf.GetNumberOfPages();
            var fontPath = GetJapaneseFontPath();

            var font =
                PdfFontFactory.CreateFont(
                    fontPath + ",0",
                    PdfEncodings.IDENTITY_H,
                    PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);

            for (int pageIndex = 1; pageIndex <= pageCount; pageIndex++)
            {
                var sourcePage = srcPdf.GetPage(pageIndex);
                var originalSize = sourcePage.GetPageSize();

                //--------------------------------------------------
                // ページサイズ決定
                //--------------------------------------------------

                iText.Kernel.Geom.PageSize newSize;

                if (sideBySide)
                {
                    newSize =
                        new iText.Kernel.Geom.PageSize(
                            originalSize.GetWidth() * 2,
                            originalSize.GetHeight());
                }
                else
                {
                    newSize =
                        new iText.Kernel.Geom.PageSize(
                            originalSize);
                }

                var newPage = destPdf.AddNewPage(newSize);

                var canvasPdf = new PdfCanvas(newPage);

                float offsetX = 0;

                //--------------------------------------------------
                // 原文コピー
                //--------------------------------------------------
                var pageCopy =
                        sourcePage.CopyAsFormXObject(destPdf);

                canvasPdf.AddXObjectAt(
                    pageCopy,
                    0,
                    0);
                if (sideBySide)
                {
                    canvasPdf.AddXObjectAt(
                        pageCopy,
                        originalSize.GetWidth(),
                        0);

                    offsetX =
                        originalSize.GetWidth();
                }

                var layoutCanvas =
                    new iText.Layout.Canvas(
                        canvasPdf,
                        newSize);

                var blocks =
                    textBlocks
                    .Where(b => b.PageIndex == pageIndex - 1)
                    .OrderByDescending(b => b.Y)
                    .ToList();

                //--------------------------------------------------
                // 元のテキスト白塗り
                //--------------------------------------------------

                foreach (var tb in blocks)
                {
                    if (tb.IsImage)
                        continue;
                    float x = tb.X + offsetX;
                    float y = tb.Y;
                    float w = tb.Width;
                    float h = tb.Height;

                    if (w <= 0 || h <= 0)
                        continue;


                    canvasPdf.SaveState();

                    canvasPdf.SetFillColor(
                        new iText.Kernel.Colors.DeviceRgb(
                            255,
                            255,
                            255));

                    canvasPdf.Rectangle(
                        x,
                        y,
                        w,
                        h);

                    canvasPdf.Fill();

                    canvasPdf.RestoreState();
                }

                //--------------------------------------------------
                // 翻訳文挿入
                //--------------------------------------------------

                foreach (var tb in blocks)
                {
                    if (string.IsNullOrEmpty(tb.TranslatedText))
                        continue;

                    float x = tb.X + offsetX;

                    float y = tb.Y;

                    float w = tb.Width;

                    float h = tb.Height;

                    if (w <= 0 || h <= 0)
                        continue;

                    float maxFontSize =
                        Math.Clamp(
                            tb.FontSize *1.5f,
                            8f,
                            12f);

                    float minFontSize =
                        6f;

                    float fontSize =
                        maxFontSize;

                    Paragraph paragraph;

                    //--------------------------------------------------
                    // フォントサイズ自動調整
                    //--------------------------------------------------

                    while (true)
                    {
                        paragraph =
                            new Paragraph(
                                tb.TranslatedText)
                                .SetFont(font)
                                .SetFontSize(fontSize)
                                .SetMargin(0)
                                .SetPadding(0)
                                .SetMultipliedLeading(1.1f);

                        var renderer =
                            paragraph.CreateRendererSubTree();

                        renderer.SetParent(
                            layoutCanvas.GetRenderer());

                        var layoutContext =
                            new iText.Layout.Layout.LayoutContext(
                                new iText.Layout.Layout.LayoutArea(
                                    pageIndex,
                                    new iText.Kernel.Geom.Rectangle(
                                        x,
                                        y,
                                        w,
                                        h)));

                        var result =
                            renderer.Layout(
                                layoutContext);

                        if (result != null &&
                            result.GetStatus()
                                == iText.Layout.Layout.LayoutResult.FULL)
                        {
                            break;
                        }

                        if (fontSize <= minFontSize)
                        {
                            break;
                        }

                        fontSize -= 0.5f;
                    }

                    //--------------------------------------------------
                    // 描画
                    //--------------------------------------------------

                    var rect =
                        new iText.Kernel.Geom.Rectangle(
                            x,
                            y,
                            w,
                            h);

                    var innerCanvas =
                        new iText.Layout.Canvas(
                            canvasPdf,
                            rect);

                    innerCanvas.Add(
                        paragraph);

                    innerCanvas.Close();
                }

                layoutCanvas.Close();
            }

            return output;
        }

        private static string GetJapaneseFontPath()
        {
            string fontsDir =
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.Fonts);

                    var candidates = new[]
                    {
                "meiryo.ttc",     
                "YuGothR.ttc",    
                "msgothic.ttc",   
                "arialuni.ttf"    
            };

            foreach (var name in candidates)
            {
                var path = Path.Combine(fontsDir, name);

                if (File.Exists(path))
                    return path;
            }

            throw new FileNotFoundException(
                "No usable Japanese font found.");
        }

        private void UpdateApiKeyButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new ApiKeyWindow();
            window.Owner = this;

            var result = window.ShowDialog();

            if (result == true)
            {
                MessageBox.Show("APIキーを更新しました。", "完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void RemoveSingleItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string item)
            {
                selectedPdfPaths.Remove(item);
                SelectedPdfList.Items.Remove(item);
            }
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            Properties.Settings.Default.SideBySide =
                SideBySideCheckBox.IsChecked == true;

            Properties.Settings.Default.Save();

            base.OnClosing(e);
        }

    }
}
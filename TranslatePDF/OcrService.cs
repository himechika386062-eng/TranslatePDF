using System;
using System.IO;
using System.Text.RegularExpressions;
using Tesseract;

namespace TranslatePDF.Services
{
    public static class OcrService
    {
        private static TesseractEngine engine;

        static OcrService()
        {
            var tessPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

            engine = new TesseractEngine(
                tessPath,
                "eng",
                EngineMode.Default);
        }

        /// <summary>
        /// 画像からテキストを読み取ります。
        /// </summary>
        /// <param name="imageBytes">画像データ</param>
        /// <param name="mode">PageSegMode (6: 段落, 7: 一行, 3: 自動)</param>
        public static string ReadText(byte[] imageBytes, int mode = 6)
        {
            try
            {
                using var img = Pix.LoadFromMemory(imageBytes);

                // (PageSegMode)mode で int から列挙型へ変換
                using var page = engine.Process(img, (PageSegMode)mode);

                var text = page.GetText();
                if (string.IsNullOrWhiteSpace(text)) return "";

                // OCR特有のゴミ（連続する記号など）を簡易クリーニング
                text = Regex.Replace(text, @"[|¦_]{2,}", "");
                return text?.Trim() ?? "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OCR Error: {ex.Message}");
                return "";
            }
        }
    }
}
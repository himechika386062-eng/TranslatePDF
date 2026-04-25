using System;
using System.IO;
using Tesseract;

namespace TranslatePDF.Services
{
    public static class OcrService
    {
        private static TesseractEngine engine;

        static OcrService()
        {
            var tessPath =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "tessdata");

            engine =
                new TesseractEngine(
                    tessPath,
                    "eng+fra+spa+nld+ita+rus+mon+por+tha+chi_sim+chi_sim_vert+chi_tra+chi_tra_vert+jpn+jpn_vert+kor+kor_vert",
                    EngineMode.Default);
        }

        public static string ReadText(
            byte[] imageBytes)
        {
            try
            {
                using var ms =
                    new MemoryStream(imageBytes);

                using var img =
                    Pix.LoadFromMemory(
                        ms.ToArray());

                using var page =
                    engine.Process(img);

                return page.GetText();
            }
            catch
            {
                return "";
            }
        }
    }
}
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TranslatePDF.Services
{
    public static class OcrService
    {
        public static List<OcrItem> ReadOcrText(Bitmap bitmap)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // python.exe
            string pythonPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(baseDir, @"..\..\..\..\..\paddle_env\Scripts\python.exe"));

            // ocr.py
            string scriptPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(baseDir, @"..\..\..\..\..\tools\ocr.py"));
            //var fullImagePath = Path.GetFullPath(imagePath).Replace("\\", "/");
            bitmap = new Bitmap(bitmap, new Size(bitmap.Width * 2, bitmap.Height * 2));
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            byte[] imageBytes = ms.ToArray();

            string tempBase64 = Convert.ToBase64String(imageBytes);

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{scriptPath}\" --base64",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };

            process.Start();

            //stdinに流す
            using (var writer = process.StandardInput)
            {
                writer.Write(tempBase64);
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.WriteLine("Python OCR Error: " + error);
            }
            try
            {
                var result = JsonSerializer.Deserialize<List<OcrItem>>(output);
                return result ?? new List<OcrItem>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("JSON Parse Error: " + ex.Message);
                Debug.WriteLine(output);
                return new List<OcrItem>();
            }
        }
    }
}

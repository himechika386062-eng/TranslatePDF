//#define OCR
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Layout;
using iText.StyledXmlParser.Jsoup.Parser;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using TranslatePDF.Services;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Menu;

namespace TranslatePDF.Services
{
    public static class PdfReaderService
    {
        public static List<TextBlock> ReadLines(string pdfPath, int from, int to)
        {
            var allParagraphs = new List<TextBlock>();
            var rects = new List<TextBlock>();
            using var pdf = new PdfDocument(new PdfReader(pdfPath));
            int totalPages = pdf.GetNumberOfPages();

            int startPage = Math.Max(1, from);
            int endPage = Math.Min(totalPages, to);
            if (startPage > endPage)
                return new List<TextBlock>();

            for (int pageIndex = startPage; pageIndex <= endPage; pageIndex++)
            {
#if OCR
                var page = pdf.GetPage(pageIndex);
                var crop = page.GetCropBox();

                float cropX = crop.GetX();
                float cropY = crop.GetY();

                List<TextBlock> charBlocks = null;
                Bitmap bitmap = null;

                try
                {
                    var listener = new MyTextEventListener();
                    var parser = new PdfCanvasProcessor(listener);
                    parser.ProcessPageContent(page);
                    charBlocks = listener.GetBlocks();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                    charBlocks = null;
                }

                bool hasValidText =
                    charBlocks != null &&
                    charBlocks.Count > 0 &&
                    charBlocks.Any(b => !string.IsNullOrWhiteSpace(b.Text));

                if (!hasValidText)//OCR
                {
                    bitmap = RenderPageToBitmap(pdfPath, pageIndex);
                    rects.AddRange(DetectTextRects(bitmap, pageIndex, pdfPath));
                    //bitmap = RefineBitmap(bitmap);

                    var ocr = OcrService.ReadOcrText(bitmap);

                    //var ch = ConvertOcrToBlocks(ocr, pageIndex);
                    //追加
                }
                //座標補正
                /*
                foreach (var b in charBlocks)
                {
                    b.X -= cropX;
                    b.Y -= cropY;
                    if (b.Height > 20f) b.Height *= 0.7f;
                }
                */
#else
                var page = pdf.GetPage(pageIndex);
                var crop = page.GetCropBox();

                float cropX = crop.GetX();
                float cropY = crop.GetY();
                float pdfHeight = crop.GetHeight();

                // IEventListener を使って文字座標取得
                var listener = new MyTextEventListener();
                var parser = new PdfCanvasProcessor(listener);

                parser.ProcessPageContent(page);
                var charBlocks = listener.GetBlocks();

                //座標補正
                foreach (var b in charBlocks)
                {
                    b.X -= cropX;
                    b.Y -= cropY;
                    if (b.Height > 20f) b.Height *= 0.7f;
                }

                using var bitmap = RenderPageToBitmap(pdfPath, pageIndex);

                rects.AddRange(DetectTextRects(bitmap, pageIndex, pdfPath));
#endif
                foreach (var rect in rects.Where(r => r.PageIndex == pageIndex - 1))
                {
                    
                    var inside = charBlocks
                        .Where(cb => IsInside(cb, rect))
                        .OrderBy(cb => cb.Y)
                        .ThenBy(cb => cb.X)
                        .ToList();

                    if (inside.Count == 0)
                    {

                        rect.Text = "";
                        continue;
                    }

                    var lineBlock = MergeLine(inside, rect);

                    rect.Text = lineBlock.Text;
                }
                rects.RemoveAll(r => !r.IsImage && string.IsNullOrWhiteSpace(r.Text));
            }       
            return rects;
        }


        #region OCR
#if OCR
        //テキスト取得
        private static List<TextBlock> ConvertOcrToBlocks(List<OcrItem> ocrItems, int pageIndex)
        {
            var list = new List<TextBlock>();

            foreach (var item in ocrItems)
            {
                if (item.Box == null || item.Box.Count == 0)
                    continue;

                float minX = item.Box.Min(p => p[0]);
                float minY = item.Box.Min(p => p[1]);
                float maxX = item.Box.Max(p => p[0]);
                float maxY = item.Box.Max(p => p[1]);

                list.Add(new TextBlock
                {
                    PageIndex = pageIndex - 1,
                    Text = item.Text,
                    X = minX,
                    Y = minY,
                    Width = maxX - minX,
                    Height = maxY - minY,
                    FontSize = 0,
                    IsImage = false
                });
            }

            return list;
        }
#endif
#endregion

        public static Bitmap RenderPageToBitmap(string pdfPath, int pageIndex)
        {
            using var pdf = new PdfDocument(new PdfReader(pdfPath));
            var page = pdf.GetPage(pageIndex);
            var pageSize = page.GetPageSize();

            float pdfWidth = pageSize.GetWidth();
            float pdfHeight = pageSize.GetHeight();

            float dpi = 300f;
            float scale = dpi / 72f;

            int width = (int)Math.Round(pdfWidth * scale);
            int height = (int)Math.Round(pdfHeight * scale);

            using var doc = PdfiumViewer.PdfDocument.Load(pdfPath);

            using var img = doc.Render(
                pageIndex - 1,
                width,
                height,
                PdfiumViewer.PdfRenderFlags.Annotations);

            return new Bitmap(img);
        }

        static bool IsInside(TextBlock text, TextBlock rect)
        {
            return
                text.X >= rect.X &&
                text.Y >= rect.Y &&
                text.X + text.Width <= rect.X + rect.Width &&
                text.Y + text.Height <= rect.Y + rect.Height;
        }
        private static TextBlock MergeLine(List<TextBlock> chars, TextBlock rect)
        {
            var filtered = chars
                .ToList();

            // もし全部除外されたら安全に戻る
            if (filtered.Count == 0)
            {
                return new TextBlock
                {
                    Text = "",
                    X = 0,
                    Y = 0,
                    Width = 0,
                    Height = 0,
                    FontSize = 0,
                    IsImage = false,
                };
            }
            var ordered = filtered
                .OrderByDescending(c => c.Y)
                .ThenBy(c => c.X)
                .ToList();

            var sb = new System.Text.StringBuilder();

            float minWidth = ordered.Min(c => c.Width);
            float avgHeight = ordered.Average(c => c.Height);

            for (int i = 0; i < ordered.Count; i++)
            {
                var current = ordered[i];

                if (i > 0)
                {
                    var prev = ordered[i - 1];

                    float vGap = prev.Y - current.Y;
                    float hGap = Math.Abs(
                        current.X
                        - (prev.X + prev.Width));

                    // ===== 縦方向 gap =====

                    if (vGap > avgHeight * 1.5f)
                    {
                        sb.Append("\n");
                    }

                    // ===== 段落 =====

                    if (Math.Abs((rect.X+rect.Width) - (prev.X + prev.Width)) > minWidth * 20.0f)
                    {
                        if (current.X < prev.X)
                        {
                            sb.Append("\n");
                        }
                    }

                    // ===== 横方向 gap =====

                    if (hGap > minWidth * 0.6f)
                    {
                        sb.Append(" ");
                    }
                }

                sb.Append(current.Text);
            }

            return new TextBlock
            {
                Text = sb.ToString(),
                X = ordered.Min(c => c.X),
                Y = ordered.Max(c => c.Y),
                Width =
            ordered.Max(c => c.X + c.Width)
            - ordered.Min(c => c.X),
                Height = ordered.Max(c => c.Height),
                FontSize = ordered.Max(c => c.FontSize),
                IsImage = false,
            };
        }

        //:::::::::::::::::::::::::::::::::::::::::::::::::::::::
        //テキストブロック検出
        //:::::::::::::::::::::::::::::::::::::::::::::::::::::::

        public static List<TextBlock> DetectTextRects(Bitmap bitmap, int pageIndex, string pdfPath)
        {
            var results = new List<TextBlock>();

            using var src = bitmap.ToMat();

            // =========================
            // 1. グレースケール
            // =========================

            using var gray = new Mat();
            CvInvoke.CvtColor(src, gray, ColorConversion.Bgr2Gray);

            // =========================
            // 2. 二値化
            // =========================

            using var thresh = new Mat();
            CvInvoke.Threshold(
                gray,
                thresh,
                180,
                255,
                ThresholdType.BinaryInv);

            // =========================
            // 3. 行を作る（横）
            // =========================
            
            var kernelLine = new Mat(1, 8, DepthType.Cv8U, 1);
            kernelLine.SetTo(new MCvScalar(1));

            CvInvoke.Dilate(
                thresh,
                thresh,
                kernelLine,
                new System.Drawing.Point(-1, -1),
                1,
                BorderType.Default,
                new MCvScalar());
            //*******************DEBUG用*********************
            /*
            SaveMatAsPdf(thresh, @"C:\\Users\\User\\Downloads\\debug_dilate1_" + pageIndex.ToString() + ".pdf");
            Process.Start(new ProcessStartInfo
            {
                FileName = @"C:\Users\User\Downloads\debug_dilate1_" + pageIndex.ToString() + ".pdf",
                UseShellExecute = true   // これがないと開けない
            });
            */
            // =========================
            // 4. 段落を作る（縦）
            // =========================
            
            var kernelParagraph = new Mat(10, 1, DepthType.Cv8U, 1);
            kernelParagraph.SetTo(new MCvScalar(1));

            CvInvoke.Dilate(
                thresh,
                thresh,
                kernelParagraph,
                new System.Drawing.Point(-1, -1),
                1,
                BorderType.Default,
                new MCvScalar());
            //*******************DEBUG用*********************
            /*
            SaveMatAsPdf(thresh, @"C:\\Users\\User\\Downloads\\debug_dilate2_" + pageIndex.ToString() + ".pdf");
            Process.Start(new ProcessStartInfo
            {
                FileName = @"C:\Users\User\Downloads\debug_dilate2_" + pageIndex.ToString() + ".pdf",
                UseShellExecute = true   // これがないと開けない
            });
            */
            // =========================
            // 5. 輪郭検出
            // =========================

            using var contours = new VectorOfVectorOfPoint();

            CvInvoke.FindContours(
                thresh,
                contours,
                null,
                RetrType.External,
                ChainApproxMethod.ChainApproxSimple);
            //*******************DEBUG用*********************
            /*
            SaveMatAsPdf(thresh, @"C:\\Users\\User\\Downloads\\debug_dilate3_" + pageIndex.ToString() + ".pdf");
            Process.Start(new ProcessStartInfo
            {
                FileName = @"C:\Users\User\Downloads\debug_dilate3_" + pageIndex.ToString() + ".pdf",
                UseShellExecute = true
            });
            */
            int pageArea = bitmap.Width * bitmap.Height;
            //List<System.Drawing.Rectangle> rects = new List<System.Drawing.Rectangle>();
            for (int i = 0; i < contours.Size; i++)
            {
#if OCR
                var rotRect = CvInvoke.MinAreaRect(contours[i]);
                float angle = rotRect.Angle;
                float an2 = Math.Abs(angle - 5) > 10 ? 0 : 5;
                PointF[] pts = rotRect.GetVertices();

                float minX = pts.Min(p => p.X);
                float maxX = pts.Max(p => p.X);
                float minY = pts.Min(p => p.Y);
                float maxY = pts.Max(p => p.Y);

                int x = (int)Math.Floor(minX);
                int y = (int)Math.Floor(minY);
                int w = (int)Math.Ceiling(maxX - minX);
                int h = (int)Math.Ceiling(maxY - minY);

                var r = new System.Drawing.Rectangle(x,y,w,h);
#else
                var r = CvInvoke.BoundingRectangle(contours[i]);
#endif

                int area = r.Width * r.Height;

                if (area < 150)
                    continue;

                if (area > pageArea * 0.9)
                    continue;

                float aspect = (float)r.Width / r.Height;

                if (aspect > 20  || aspect <0.05)
                    continue;

                results.Add(new TextBlock
                {
                    PageIndex = pageIndex - 1,
                    X = r.X,
                    Y = r.Y,
                    Width = r.Width,
                    Height = r.Height
                });
            }

            // =========================
            // 6. 画像の情報を取得
            // =========================
            float pageHeight = bitmap.Height;
#if OCR
#else
            var imageBlocks =
                DetectImageRects(
                    pdfPath,
                    pageIndex,
                    pageHeight);
            results.AddRange(imageBlocks);
#endif
            // =========================
            // 7. 上から順に並べて干渉をチェックする
            // =========================

            var sorted = results
                .OrderBy(r => r.Y)
                .ThenBy(r => r.X)
                .ToList();

            if (sorted.Count == 0)
                return sorted;

            float avgHeight = sorted.Average(b => b.Height);

            var final = new List<TextBlock>();
            
            for (int i = 0; i < sorted.Count; i++)
            {
                var current = sorted[i];

                var currentRects =
                    new List<System.Drawing.Rectangle>
                    {
                        new System.Drawing.Rectangle(
                            (int)current.X,
                            (int)current.Y,
                            (int)current.Width,
                            (int)current.Height)
                    };

                for (int j = 0; j < final.Count; j++)
                {
                    var existing = final[j];

                    var existingRect =
                        new System.Drawing.Rectangle(
                            (int)existing.X,
                            (int)existing.Y,
                            (int)existing.Width,
                            (int)existing.Height);

                    var newRects =
                        new List<System.Drawing.Rectangle>();

                    foreach (var r in currentRects)
                    {
                        if (!r.IntersectsWith(existingRect))
                        {
                            newRects.Add(r);
                            continue;
                        }

                        float ratioCurrent =
                            OverlapRatio(
                                r,
                                existingRect);

                        float ratioExisting =
                            OverlapRatio(
                                existingRect,
                                r);
                        int gap = 0;

                        if (existing.IsImage && !current.IsImage)
                        {
                            // existing は画像 → 絶対残す
                            // current を分割
                            var pieces =
                                SplitRectangle(
                                    r,
                                    existingRect);

                            foreach (var p in pieces)
                            {
                                var s = Shrink(p, gap);

                                if (IsValid(s))
                                    newRects.Add(s);
                            }

                            continue;
                        }

                        if (!existing.IsImage && current.IsImage)
                        {
                            // current は画像 → 既存テキストを分割
                            var pieces =
                                SplitRectangle(
                                    existingRect,
                                    r);

                            final.RemoveAt(j);
                            j--;

                            foreach (var p in pieces)
                            {
                                var s = Shrink(p, gap);

                                if (!IsValid(s))
                                    continue;

                                final.Add(
                                    new TextBlock
                                    {
                                        PageIndex = existing.PageIndex,
                                        X = s.X,
                                        Y = s.Y,
                                        Width = s.Width,
                                        Height = s.Height,
                                        IsImage = existing.IsImage
                                    });
                            }

                            newRects.Add(r);

                            continue;
                        }

                        if (ratioExisting >= ratioCurrent)
                        {
                            // existing を残す
                            // current を分割
                            var pieces =
                                SplitRectangle(
                                    r,
                                    existingRect);

                            foreach (var p in pieces)
                            {
                                var s = Shrink(p, gap);

                                if (IsValid(s))
                                    newRects.Add(s);
                            }
                        }
                        else
                        {
                            // current を残す
                            // existing を分割
                            var pieces =
                                SplitRectangle(
                                    existingRect,
                                    r);

                            final.RemoveAt(j);
                            j--;

                            foreach (var p in pieces)
                            {
                                var s = Shrink(p, gap);

                                if (!IsValid(s))
                                    continue;

                                final.Add(
                                    new TextBlock
                                    {
                                        PageIndex = existing.PageIndex,
                                        X = s.X,
                                        Y = s.Y,
                                        Width = s.Width,
                                        Height = s.Height,
                                        IsImage = existing.IsImage
                                    });
                            }

                            newRects.Add(r);
                        }
                    }

                    currentRects = newRects;

                    if (currentRects.Count == 0)
                        break;
                }

                foreach (var r in currentRects)
                {
                    final.Add(
                        new TextBlock
                        {
                            PageIndex =
                                current.PageIndex,
                            X = r.X,
                            Y = r.Y,
                            Width = r.Width,
                            Height = r.Height,
                            IsImage = current.IsImage
                        });
                }
            }
            //*******************DEBUG用*********************
            /*
            using (var g = Graphics.FromImage(bitmap))
            {
                using var pen = new Pen(Color.Red, 2);

                foreach (var b in final)
                {
                    g.DrawRectangle(
                        pen,
                        b.X,
                        b.Y,
                        b.Width,
                        b.Height);
                }
            }
            
            bitmap.Save($@"C:\Users\User\Downloads\debug_rect_{pageIndex}.png");
            string path = @"C:\Users\User\Downloads\debug_rect_" + pageIndex + ".pdf";
            using var matWithRects = bitmap.ToMat();
            SaveMatAsPdf(matWithRects, path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            */
            /////////////////////////ここまで//////////////////////
            //座標変換  
            foreach (var block in final)
            {
                block.Y =
                    pageHeight
                    - block.Y
                    - block.Height;
            }

            return final;
        }
        //Image取得
        #region
        public static List<TextBlock> DetectImageRects(
            string pdfPath,
            int pageIndex,
            float pageHeight)
        {
            var results = new List<TextBlock>();

            using var pdf =
                new PdfDocument(
                    new PdfReader(pdfPath));

            var page = pdf.GetPage(pageIndex);

            var listenerImage =
                new SimpleImageListener(
                    results,
                    pageIndex);

            var parserImage =
                new PdfCanvasProcessor(listenerImage);

            parserImage.ProcessPageContent(page);

            var listenerText = new MyTextEventListener();

            var parserText = new PdfCanvasProcessor(listenerText);

            parserText.ProcessPageContent(page);

            var charBlocks = listenerText.GetBlocks();

            var filtered = new List<TextBlock>();
            // 座標変換
            foreach (var block in results)
            {
                int count = 0;

                foreach (var cb in charBlocks)
                {
                    if (IsInside(cb, block))
                    {
                        count++;
                        if(count > 5)
                        {
                            break;
                        }
                    }
                }
                if (count <= 5)
                {
                    block.Y =
                        pageHeight
                        - block.Y
                        - block.Height;
                    block.IsImage = true;
                    filtered.Add(block);
                }
            }
            return filtered;
        }
        
        private class SimpleImageListener : IEventListener
        {
            private readonly List<TextBlock> _results;
            private readonly int _pageIndex;

            public SimpleImageListener(
                List<TextBlock> results,
                int pageIndex)
            {
                _results = results;
                _pageIndex = pageIndex;
            }

            public void EventOccurred(
                IEventData data,
                EventType type)
            {
                if (type != EventType.RENDER_IMAGE)
                    return;

                var info =
                    (ImageRenderInfo)data;

                var image =
                    info.GetImage();

                if (image == null)
                    return;

                var ctm =
                    info.GetImageCtm();

                float x = ctm.Get(Matrix.I31);
                float y = ctm.Get(Matrix.I32);

                // ここ重要（負になることがある）
                float w = Math.Abs(ctm.Get(Matrix.I11));
                float h = Math.Abs(ctm.Get(Matrix.I22));

                _results.Add(
                    new TextBlock
                    {
                        PageIndex = _pageIndex - 1,
                        X = x,
                        Y = y,
                        Width = w,
                        Height = h,
                        IsImage = true
                    });
            }

            public ICollection<EventType> GetSupportedEvents()
                => new[]
                {
            EventType.RENDER_IMAGE
                };
        }
        #endregion

        //以下、短形の干渉に関して
        #region
        static float OverlapRatio(System.Drawing.Rectangle a, System.Drawing.Rectangle b)
        {
            var inter =
                System.Drawing.Rectangle.Intersect(a, b);

            if (inter.IsEmpty)
                return 0f;

            float interArea =
                inter.Width * inter.Height;

            float areaA =
                a.Width * a.Height;

            if (areaA == 0)
                return 0f;

            return interArea / areaA;
        }
        static List<System.Drawing.Rectangle> SplitRectangle(System.Drawing.Rectangle target, System.Drawing.Rectangle cutter)
        {
            var result =
                new List<System.Drawing.Rectangle>();

            var overlap =
                System.Drawing.Rectangle.Intersect(
                    target,
                    cutter);

            if (overlap.IsEmpty)
            {
                result.Add(target);
                return result;
            }

            // 上
            int topHeight =
                overlap.Top - target.Top;

            if (topHeight > 0)
            {
                result.Add(
                    new System.Drawing.Rectangle(
                        target.X,
                        target.Y,
                        target.Width,
                        topHeight));
            }

            // 下
            int bottomHeight =
                target.Bottom - overlap.Bottom;

            if (bottomHeight > 0)
            {
                result.Add(
                    new System.Drawing.Rectangle(
                        target.X,
                        overlap.Bottom,
                        target.Width,
                        bottomHeight));
            }

            // 左
            int leftWidth =
                overlap.Left - target.Left;

            if (leftWidth > 0)
            {
                result.Add(
                    new System.Drawing.Rectangle(
                        target.X,
                        overlap.Top,
                        leftWidth,
                        overlap.Height));
            }

            // 右
            int rightWidth =
                target.Right - overlap.Right;

            if (rightWidth > 0)
            {
                result.Add(
                    new System.Drawing.Rectangle(
                        overlap.Right,
                        overlap.Top,
                        rightWidth,
                        overlap.Height));
            }

            return result;
        }
        static bool IsValid(System.Drawing.Rectangle r)
        {
            if (r.Width < 3)
                return false;

            if (r.Height < 3)
                return false;

            return true;
        }
        static System.Drawing.Rectangle Shrink(System.Drawing.Rectangle r, int gap)
        {
            int shrink = gap / 2;

            r.Inflate(-shrink, -shrink);

            if (r.Width <= 0 || r.Height <= 0)
                return System.Drawing.Rectangle.Empty;

            return r;
        }
        #endregion
        //デバッグ用関数
        #region
        public static void SaveMatAsPdf(Mat mat, string path)
        {
            if (mat == null || mat.IsEmpty)
                return;

            using var ms = new MemoryStream();

            // Mat → PNG
            mat.ToImage<Emgu.CV.Structure.Gray, byte>()
               .ToBitmap()
               .Save(ms, System.Drawing.Imaging.ImageFormat.Png);

            var imageData =
                ImageDataFactory.Create(ms.ToArray());

            using var writer =
                new PdfWriter(path);

            using var pdf =
                new PdfDocument(writer);

            var pageSize =
                new PageSize(
                    imageData.GetWidth(),
                    imageData.GetHeight());

            var document =
                new Document(pdf, pageSize);

            var img =
                new iText.Layout.Element.Image(imageData);

            img.SetFixedPosition(0, 0);

            document.Add(img);

            document.Close();
        }
        #endregion
    }
}
namespace TranslatePDF
{
    public class TextBlock
    {
        public int PageIndex { get; set; }

        public string Text { get; set; }

        public string TranslatedText { get; set; }

        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }

        public bool IsImage { get; set; }

        public float FontSize { get; set; }

    }

    public class RectInfo
    {
        public int PageIndex { get; set; }
        public System.Drawing.Rectangle Rect { get; set; }
    }
}

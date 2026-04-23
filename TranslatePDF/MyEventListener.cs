using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

public class TextBlock
{
    public string Text;
    public float X, Y, Width, Height;
}

public class MyTextEventListener : IEventListener
{
    private List<TranslatePDF.TextBlock> _blocks = new List<TranslatePDF.TextBlock>();

    public void EventOccurred(IEventData data, EventType type)
    {
        if (data is TextRenderInfo renderInfo)
        {
            var bottomLeft = renderInfo.GetDescentLine().GetStartPoint();
            var topRight = renderInfo.GetAscentLine().GetEndPoint();

            float y1 = bottomLeft.Get(Vector.I2);
            float height = topRight.Get(Vector.I2) - y1;
            
            var rect = renderInfo.GetBaseline().GetBoundingRectangle();

            float x = rect.GetX();
            float y = rect.GetY();
            float width = rect.GetWidth();
            

            // 安定化のため、height が 0 以下になったら最低値を設定
            if (height <= 0) height = 5f;

            _blocks.Add(new TranslatePDF.TextBlock
            {
                Text = renderInfo.GetText(),
                X = x,
                Y = y,
                Width = width,
                Height = height,
                FontSize = height,
                IsImage = false
            });
        }
    }

    public ICollection<EventType> GetSupportedEvents()
    {
        return new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    public List<TranslatePDF.TextBlock> GetBlocks() => _blocks;
}
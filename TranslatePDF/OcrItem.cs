using System.Text.Json.Serialization;

namespace TranslatePDF
{
    public class OcrItem
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }

        [JsonPropertyName("box")]
        public List<List<float>> Box { get; set; }
    }
}
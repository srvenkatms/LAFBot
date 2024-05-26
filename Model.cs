namespace OperationBotWebSvc.Models
{
    public class RootObject
    {
        public string Object { get; set; }
        public List<DataObject> Data { get; set; }
        public string Model { get; set; }
        public Usage Usage { get; set; }
    }

    public class DataObject
    {
        public string Object { get; set; }
        public int Index { get; set; }
        public List<double> Embedding { get; set; }
    }

    public class Usage
    {
        public int Prompt_tokens { get; set; }
        public int Total_tokens { get; set; }
    }
    public class ContentItem
    {
        public string Content { get; set; }
        public string ContentLocation { get; set; }
        public string Title { get; set; }
    }
    public class PromtpText
    {
        public string input { get; set; }
        public string[] history { get; set; }
       
    }
    public class vectorRequestBody
    {
        public PromtpText PromtpText { get; set; }
        public RootObject RootObject { get; set; }
    }
}

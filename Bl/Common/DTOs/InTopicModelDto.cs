namespace Bl.Common.DTOs
{
    public class InTopicModelDto<T> where T : class
    {
        public T Model {  get; set; }
        public bool InTopic { get; set; }
    }
}

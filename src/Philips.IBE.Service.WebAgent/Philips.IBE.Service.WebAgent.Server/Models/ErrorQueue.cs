namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class ErrorQueue
    {
        public int ID { get; set; }
        public required byte[] Message { get; set; }
        public int SenderId { get; set; }
        public DateTime timeStamp { get; set; }
    }
}
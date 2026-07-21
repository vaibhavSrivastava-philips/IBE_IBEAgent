namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class Contract
    {
        public Acknowledgement? Acknowledgement { get; set; } 
        public HighFidelity? HighFidelity { get; set; } 
        public List<int>? InputIDs { get; set; }
        public string Name { get; set; } = string.Empty;
        public int OutputID { get; set; }
    }
}


namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class CommunicationPointModel
    {
        public List<CommunicationPoint> CommunicationPoints { get; set; }

        public CommunicationPointModel()
        {
            CommunicationPoints = new List<CommunicationPoint>();
        }
        // public CommunicationPointList CommunicationPoints { get; set; }

        // public CommunicationPointModel()
        // {
        //     CommunicationPoints = new CommunicationPointList();
        // }
    }

    // public class CommunicationPointList
    // {
    //     public List<CommunicationPoint> CommunicationPoint { get; set; }

    //     public CommunicationPointList()
    //     {
    //         CommunicationPoint = new List<CommunicationPoint>();
    //     }
    // }
}

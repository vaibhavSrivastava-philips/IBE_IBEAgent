namespace Philips.IBE.Service.WebAgent.Server.Models
{
    public class ContractModel
    {
        public List<Contract> Contracts { get; set; }

        public ContractModel()
        {
            Contracts = new List<Contract>();
        }
    }

    //public class ContractList
    //{
    //    public List<ContractData> Contract { get; set; }

    //    public ContractList()
    //    {
    //        Contract = new List<ContractData>();
    //    }
    //}

    //public class ContractData
    //{
    //    public string Name { get; set; }
    //    public WorkflowModel Workflows { get; set; }
    //}

    //public class WorkflowModel
    //{
    //    public List<Workflow> Workflow { get; set; }

    //    public WorkflowModel()
    //    {
    //        Workflow = new List<Workflow>();
    //    }
    //}
}

namespace Philips.IBE.IBEAgent.Abstractions;

public interface IContractResolver         // pure decision: exactly one contract (INV-3).
{
    IContractRuntime Resolve(MessageContext context);
}

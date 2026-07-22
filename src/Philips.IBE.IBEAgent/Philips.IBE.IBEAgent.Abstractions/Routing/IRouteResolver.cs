namespace Philips.IBE.IBEAgent.Abstractions;

public interface IRouteResolver            // pure decision: exactly one contract (INV-3).
{
    IContractRuntime Resolve(MessageContext context);
}
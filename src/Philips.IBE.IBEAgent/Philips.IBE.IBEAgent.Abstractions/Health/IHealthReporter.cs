namespace Philips.IBE.IBEAgent.Abstractions;

public interface IHealthReporter
{
    HealthSnapshot GetSnapshot();
}

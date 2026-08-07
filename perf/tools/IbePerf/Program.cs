using IbePerf;

// IbePerf: black-box performance driver for the IBE Agent.
//   IbePerf load   --scenario s.json --contract contractData.json --out <dir>
//   IbePerf sink   --scenario s.json --contract contractData.json --out <dir> --stop <stopFile> [--ready <readyFile>]
//   IbePerf report --session <sessionDir> --out <session.html> [--baseline <dir>]
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: IbePerf <load|sink|report> [--key value ...]");
    return 2;
}

var verb = args[0].ToLowerInvariant();
var cli = Cli.Parse(args.AsSpan(1));

try
{
    return verb switch
    {
        "load" => await LoadVerb.RunAsync(cli),
        "sink" => await SinkVerb.RunAsync(cli),
        "report" => ReportVerb.Run(cli),
        _ => Bad($"unknown verb '{verb}'"),
    };
}
catch (OperationCanceledException)
{
    return 0; // graceful stop
}
catch (Exception ex)
{
    Console.Error.WriteLine($"IbePerf {verb} failed: {ex}");
    return 1;
}

static int Bad(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

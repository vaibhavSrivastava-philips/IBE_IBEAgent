using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public class HeartBeatService : IHeartBeatService
    {
        private readonly ILogger<HeartBeatService> _logger;


        public HeartBeatService(ILogger<HeartBeatService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> IsPortOpenAsync(string host, int port)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    await client.ConnectAsync(host, port);
                    var logHost = host.Replace("\r", "").Replace("\n", "");
                    _logger.LogInformation("Successfully connected to {Host}:{Port}", logHost, port);
                    return true;
                }
            }
            catch (SocketException ex)
            {
                var logHost = host.Replace("\r", "").Replace("\n", "");
                _logger.LogWarning(ex, "Failed to connect to {Host}:{Port}", logHost, port);
                return false;
            }
        }

        public List<string> GetTcpPorts()
        {
            try
            {
                using (var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netstat",
                        Arguments = "-a -n",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                })
                {

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var tcpLines = output.Split(Environment.NewLine)
                                         .Where(line => line.Contains("TCP"))
                                         .Select(line =>
                                         {
                                             var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                             return parts[1].Split(':')[1];
                                         })
                                         .Distinct()
                                         .ToList();

                    _logger.LogInformation("Successfully retrieved TCP ports.");
                    return tcpLines;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while retrieving TCP ports.");
                return new List<string>();
            }
        }
    }
}

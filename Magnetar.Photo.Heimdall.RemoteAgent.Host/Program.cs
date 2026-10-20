using System.Text.Json;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;
using Magnetar.Photo.Heimdall.RemoteAgent.Host;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    var original = Environment.GetEnvironmentVariable("SSH_ORIGINAL_COMMAND") ?? string.Empty;
    var encoded = ForcedRpcCommandParser.ExtractToken(args, original);
    var json = ForcedRpcCommandParser.DecodeRequestToken(encoded);
    var configFile = Environment.GetEnvironmentVariable("HEIMDALL_AGENT_CONFIG")
        ?? throw new InvalidOperationException("HEIMDALL_AGENT_CONFIG is not set.");
    var config = JsonSerializer.Deserialize<AgentConfiguration>(await File.ReadAllTextAsync(configFile), jsonOptions)
        ?? throw new InvalidDataException("Agent configuration is invalid.");
    using var agent = new RemoteAgent(config);
    var reply = await agent.DispatchAsync(json);
    Console.Out.WriteLine(JsonSerializer.Serialize(reply, jsonOptions));
    return 0;
}
catch (Exception)
{
    Console.Out.WriteLine(ForcedRpcCommandParser.SerializeFailure(jsonOptions));
    return 1;
}

using System.Text.Json;

namespace MapEngine.Agent;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    JsonRpcResponse Execute(JsonElement? parameters);
}

public sealed class AgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IAgentTool tool)
    {
        _tools[tool.Name] = tool;
    }

    public JsonRpcResponse Dispatch(JsonRpcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Method))
            return JsonRpcResponse.Fail(request.Id, RpcErrorCodes.InvalidRequest, "Method is required");

        if (request.Method == "tools.list")
            return JsonRpcResponse.Success(request.Id, ListTools());

        if (!_tools.TryGetValue(request.Method, out var tool))
            return JsonRpcResponse.Fail(request.Id, RpcErrorCodes.MethodNotFound, $"Unknown method: {request.Method}");

        try
        {
            var result = tool.Execute(request.Params);
            result.Id = request.Id;
            return result;
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.Fail(request.Id, RpcErrorCodes.InternalError, ex.Message);
        }
    }

    private List<ToolInfo> ListTools()
    {
        return _tools.Values.Select(t => new ToolInfo { Name = t.Name, Description = t.Description }).ToList();
    }

    private sealed class ToolInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }
}

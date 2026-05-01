#nullable enable
using System.Threading.Tasks;

namespace YuzeToolkit
{
    internal interface IMcpCommand
    {
        string Name { get; }

        bool EditorOnly { get; }

        Task<string> ExecuteAsync(McpCommandContext context);
    }
}

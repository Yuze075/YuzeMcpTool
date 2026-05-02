#nullable enable
using System.Collections.Generic;

namespace YuzeToolkit
{
    public interface IMcpTool
    {
        string Name { get; }

        string Description { get; }

        IReadOnlyList<McpToolFunctionDescriptor> Functions { get; }
    }
}

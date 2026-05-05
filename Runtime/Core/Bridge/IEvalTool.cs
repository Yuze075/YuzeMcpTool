#nullable enable
using System.Collections.Generic;

namespace YuzeToolkit
{
    public interface IEvalTool
    {
        string Name { get; }

        string Description { get; }

        IReadOnlyList<EvalToolFunctionDescriptor> Functions { get; }
    }
}

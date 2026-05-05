#nullable enable
using System;
using System.Collections.Generic;
using Puerts;

namespace YuzeToolkit
{
    [Configure]
    public sealed class ToolsPuertsCfg
    {
        [Binding]
        private static IEnumerable<Type> Bindings
        {
            get
            {
                return new List<Type>
                {
                    typeof(EditorTool),
                    typeof(AssetsTool),
                    typeof(ImportersTool),
                    typeof(ScenesTool),
                    typeof(PrefabsTool),
                    typeof(SerializedTool),
                    typeof(ProjectTool),
                    typeof(PipelineTool),
                    typeof(ValidationTool),
                    // Runtime
                    typeof(RuntimeTool),
                    typeof(ObjectsTool),
                    typeof(ComponentsTool),
                    typeof(DiagnosticsTool),
                    typeof(InspectTool),
                    typeof(ReflectionTool),
                };
            }
        }
    }
}

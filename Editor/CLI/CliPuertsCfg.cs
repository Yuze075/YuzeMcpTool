#nullable enable
using System;
using System.Collections.Generic;
using Puerts;

namespace YuzeToolkit
{
    [Configure]
    public sealed class CliPuertsCfg
    {
        [Binding]
        private static IEnumerable<Type> Bindings
        {
            get
            {
                return new List<Type>
                {
                    typeof(CliBridgeTool),
                };
            }
        }
    }
}

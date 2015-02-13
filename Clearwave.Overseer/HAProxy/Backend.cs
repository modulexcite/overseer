using System.Collections.Generic;

// Derived from: https://github.com/opserver/Opserver
namespace Clearwave.Overseer.HAProxy
{
    /// <summary>
    /// Represents an AHProxy backend for a proxy
    /// </summary>
    public class Backend : Item
    {
        public List<Server> Servers { get; internal set; }
    }
}
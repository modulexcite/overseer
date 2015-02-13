using System;

// Derived from: https://github.com/opserver/Opserver
namespace Clearwave.Overseer.HAProxy
{
    /// <summary>
    /// Represents a statistic from the proxy stat dump, since these are always added at the end in newer versions, they're parsed based on position.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public class StatAttribute : Attribute
    {
        public int Position { get; set; }
        public string Name { get; set; }

        public StatAttribute(string name, int position)
        {
            Position = position;
            Name = name;
        }
    }
}

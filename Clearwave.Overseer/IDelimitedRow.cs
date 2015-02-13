using System.Collections.Generic;

namespace Clearwave.Overseer
{
    public interface IDelimitedRow : IEnumerable<string>
    {
        int Length { get; }
        string this[int index] { get; }
    }
}

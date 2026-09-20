using PortPilot.Core.Services;

namespace PortPilot.ViewModels;

public sealed record SearchOption(SearchField Field, string Label)
{
    public override string ToString() => Label;
}

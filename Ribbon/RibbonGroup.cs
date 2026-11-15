using System.Collections.Generic;

namespace CityHelper.Ribbon;

public sealed record RibbonGroup(string Header, IReadOnlyList<RibbonCommand> Commands);

using MahjongAgent.Core.Tiles;

namespace MahjongAgent.Architecture.Tests;

public sealed class CorePlatformBoundaryTests
{
  [Fact]
  public void Core_does_not_reference_windows_desktop_assemblies()
  {
    var referencedAssemblyNames = typeof(TileType)
      .Assembly
      .GetReferencedAssemblies()
      .Select(name => name.Name)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert.DoesNotContain("PresentationCore", referencedAssemblyNames);
    Assert.DoesNotContain("PresentationFramework", referencedAssemblyNames);
    Assert.DoesNotContain("System.Windows.Forms", referencedAssemblyNames);
    Assert.DoesNotContain("WindowsBase", referencedAssemblyNames);
  }
}

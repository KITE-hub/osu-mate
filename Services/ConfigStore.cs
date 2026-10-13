using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.Services
{
  public sealed class ConfigStore
  {
    private readonly object _gate = new();

    public RootConfig Root { get; } = ConfigUtils.LoadRootConfig();

    public GlobalConfig Global => Root.Global;

    public void Update(Action<GlobalConfig> mutate)
    {
      lock (_gate)
      {
        mutate(Root.Global);
        ConfigUtils.SaveRootConfig(Root);
      }
    }

    public void Save()
    {
      lock (_gate)
      {
        ConfigUtils.SaveRootConfig(Root);
      }
    }
  }
}

namespace Oxide.Plugins
{
    [Info("No Fog", "Skipcast", "1.0.0")]
    [Description("Removes fog from the game.")]
    public class NoFog : RustPlugin
    {
        private void OnServerInitialized()
        {
            SingletonComponent<Climate>.Instance.Overrides.Fog = 0;
        }

        private void Unload()
        {
            SingletonComponent<Climate>.Instance.Overrides.Fog = -1;
        }
    }
}
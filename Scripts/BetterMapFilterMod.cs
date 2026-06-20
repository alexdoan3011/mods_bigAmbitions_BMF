using System;
using System.Threading.Tasks;
using BAModAPI;
using BetterMapFilter;

[assembly: RegisterModClass(typeof(BetterMapFilterMod))]

namespace BetterMapFilter
{
    /// <summary>
    /// Entry point for the Better Map Filter mod.
    /// Loads while in-game so it can reach the live City Map filter UI.
    /// </summary>
    [ModEntryOnInitializationLoad]
    public class BetterMapFilterMod : IModBigAmbitions
    {
        private readonly BetterMapFilterLogic _logic = new();

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _logic.Initialize(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            _logic.Shutdown();
            return Task.CompletedTask;
        }
    }
}

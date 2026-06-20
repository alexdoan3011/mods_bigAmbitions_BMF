using BAModAPI;
using UnityEngine;

namespace BetterMapFilter
{
    /// <summary>
    /// Core logic for Better Map Filter.
    /// Spawns a persistent driver (<see cref="CityMapFilterEnhancer"/>) that
    /// rebuilds the City Map filter list into a grid of toggle buttons.
    /// </summary>
    public class BetterMapFilterLogic
    {
        private ModContext _context = null!;
        private GameObject _driverObject;

        public void Initialize(ModContext context)
        {
            _context = context;

            _driverObject = new GameObject("BetterMapFilter_Driver");
            Object.DontDestroyOnLoad(_driverObject);
            var enhancer = _driverObject.AddComponent<CityMapFilterEnhancer>();
            enhancer.Configure(context);

            _context.Logger.Info("BetterMapFilter loaded. Watching for the City Map filter panel.");
        }

        public void Shutdown()
        {
            try
            {
                if (_driverObject != null)
                {
                    var enhancer = _driverObject.GetComponent<CityMapFilterEnhancer>();
                    if (enhancer != null)
                        enhancer.Teardown();

                    Object.Destroy(_driverObject);
                    _driverObject = null;
                }
            }
            catch (System.Exception e)
            {
                _context.Logger.Info($"BetterMapFilter: ignored shutdown error ({e.Message}).");
            }

            _context.Logger.Info("BetterMapFilter unloaded.");
        }
    }
}


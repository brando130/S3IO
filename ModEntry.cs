using System;
using Sims3.SimIFace;
using Sims3.Gameplay.Utilities;

namespace S3IO
{
    public delegate void Action();

    public class ModEntry
    {
        [Tunable]
        protected static bool kInstantiator = false;

        static ModEntry()
        {
            World.sOnWorldLoadFinishedEventHandler += new EventHandler(OnWorldLoadFinished);
            ModIO.Initialize();
        }

        private static void OnWorldLoadFinished(object sender, EventArgs e)
        {
            // S3IO is a shared library - no diagnostics needed, just ensure we're connected.
            // Initialization already happened in the static constructor.
        }
    }

    public class FunctionTask : Task
    {
        private Action mFunction;

        public FunctionTask(Action func)
        {
            mFunction = func;
        }

        public static ObjectGuid Perform(Action func)
        {
            return new FunctionTask(func).AddToSimulator();
        }

        public ObjectGuid AddToSimulator()
        {
            return Simulator.AddObject(this);
        }

        public override void Simulate()
        {
            try
            {
                if (mFunction != null)
                {
                    mFunction();
                }
            }
            catch (Exception ex)
            {
                // Log or handle error
            }
        }
    }
}

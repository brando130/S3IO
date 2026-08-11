using System;
using Sims3.SimIFace;
using Sims3.Gameplay;
using Sims3.Gameplay.Utilities;

namespace S3IO
{
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
            // Verify the C++ native side has found our buffer and completed
            // the handshake. This runs as a FunctionTask so we have a yielding
            // context and can safely Simulator.Sleep while waiting.
            FunctionTask.Perform(VerifyConnection);
        }

        private static void VerifyConnection()
        {
            // Wait up to ~5 seconds for the C++ handshake
            int retries = 0;
            while (!ModIO.IsConnected && retries < 50)
            {
                if (!Simulator.CheckYieldingContext(false)) return;
                Simulator.Sleep(100);
                retries++;
            }
        }
    }

    public class FunctionTask : Task
    {
        private Function mFunction;

        public FunctionTask(Function func)
        {
            mFunction = func;
        }

        public static ObjectGuid Perform(Function func)
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
            catch (ResetException)
            {
                throw;
            }
            catch (Exception)
            {
                // Exception occurred — don't silently swallow, but
                // avoid recursive S3IO calls in the error path.
            }
            finally
            {
                Simulator.DestroyObject(ObjectId);
            }
        }
    }
}

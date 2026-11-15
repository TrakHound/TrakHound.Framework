using TrakHound.Logging;

namespace TrakHound.Functions
{
    internal delegate void FunctionStatusDelegate(ITrakHoundFunction function, string runId, TrakHoundStatusItem statusItem);
}

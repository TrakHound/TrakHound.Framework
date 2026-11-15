using TrakHound.Logging;

namespace TrakHound.Functions
{
    internal delegate void FunctionLogDelegate(ITrakHoundFunction function, string runId, TrakHoundLogItem logItem);
}

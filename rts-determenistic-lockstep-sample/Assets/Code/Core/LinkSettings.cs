namespace Rts.Lockstep.Code.Core
{
    public class LinkSettings
    {
        public int LatencyMs;
        public int JitterMs;

        public LinkSettings(int latencyMs, int jitterMs)
        {
            LatencyMs = latencyMs;
            JitterMs = jitterMs;
        }
    }
}

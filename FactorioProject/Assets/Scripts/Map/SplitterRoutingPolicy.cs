namespace ProjectF.Conveyors
{
    internal struct SplitterRoutingPolicy
    {
        public int NextInput;
        public int NextOutput;

        public bool TrySelect(int requestingInput, bool leftReady, bool rightReady,
            int leftOutputs, int rightOutputs, out int output)
        {
            output = -1;
            int preferred = NextInput & 1;
            bool preferredReady = preferred == 0 ? leftReady && leftOutputs != 0 : rightReady && rightOutputs != 0;
            if (requestingInput != preferred && preferredReady)
                return false;
            bool ready = requestingInput == 0 ? leftReady : rightReady;
            int available = requestingInput == 0 ? leftOutputs : rightOutputs;
            if (!ready || available == 0)
                return false;
            int first = NextOutput & 1;
            output = (available & (1 << first)) != 0 ? first : 1 - first;
            return true;
        }

        public void Commit(int input, int output)
        {
            NextInput = 1 - input;
            NextOutput = 1 - output;
        }
    }
}

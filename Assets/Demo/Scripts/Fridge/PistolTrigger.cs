namespace EngineeringDemo
{
    internal sealed class PistolTrigger
    {
        internal enum TriggerState { Waiting, Ready, Fired }

        private double _releaseStartedAt = -1d;

        internal Hand Hand { get; private set; }
        internal TriggerState State { get; private set; }

        internal void Reset()
        {
            Hand = null;
            State = TriggerState.Waiting;
            _releaseStartedAt = -1d;
        }

        // Require a continuous release before firing again. A different hand must arm from scratch.
        internal bool Update(Hand hand, float? middleExtension, double now, float readyExtension, float fireExtension, float releaseSeconds)
        {
            if (!ReferenceEquals(hand, Hand))
            {
                Reset();
                Hand = hand;
            }
            if (hand == null || middleExtension is not float extension)
            {
                _releaseStartedAt = -1d;
                return false;
            }

            if (State != TriggerState.Ready)
            {
                if (extension < readyExtension)
                {
                    _releaseStartedAt = -1d;
                    return false;
                }

                if (_releaseStartedAt < 0d)
                    _releaseStartedAt = now;
                if (now - _releaseStartedAt >= releaseSeconds)
                    State = TriggerState.Ready;
            }
            if (State != TriggerState.Ready || extension > fireExtension)
                return false;

            State = TriggerState.Fired;
            _releaseStartedAt = -1d;
            return true;
        }
    }
}
